using System.Data;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Infrastructure;

public sealed class CourseBookingService : ICourseBookingService
{
    private static readonly TimeSpan PendingReservationLifetime = TimeSpan.FromMinutes(30);
    private readonly ApplicationDbContext _dbContext;

    public CourseBookingService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> GetAvailableSeatsAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CourseSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException("Course session was not found.");
        }

        var occupiedSeats = await CountOccupiedSeatsAsync(courseSessionId, cancellationToken);
        return session.Capacity - occupiedSeats;
    }

    public async Task<IReadOnlyCollection<CourseSessionAvailabilityDto>> GetPublishedFutureSessionsAsync(CourseType courseType, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var sessions = await (
            from course in _dbContext.Courses
            join session in _dbContext.CourseSessions on course.Id equals session.CourseId
            where course.Type == courseType
                && course.IsActive
                && session.IsPublished
                && session.StartTime > now
            select new { Course = course, Session = session })
            .ToListAsync(cancellationToken);

        var results = new List<CourseSessionAvailabilityDto>(sessions.Count);

        foreach (var item in sessions)
        {
            var occupiedSeats = await CountOccupiedSeatsAsync(item.Session.Id, cancellationToken);

            results.Add(new CourseSessionAvailabilityDto(
                item.Session.Id,
                item.Course.Id,
                item.Course.Type,
                item.Session.StartTime,
                item.Session.EndTime,
                item.Session.Capacity,
                item.Session.Capacity - occupiedSeats));
        }

        return results;
    }

    public async Task<CreateBookingResult> CreatePendingBookingAsync(CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await CreatePendingReservationAsync(
            request.CourseSessionId,
            request.CustomerName,
            request.Email,
            request.Phone,
            request.StudentProfileId,
            cancellationToken);
    }

    public async Task<CreateBookingResult> CreatePendingReservationAsync(
        Guid courseSessionId,
        string customerName,
        string email,
        string phone,
        Guid? studentProfileId = null,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);
        EnsureSessionAcceptsBooking(session);

        var existingBookings = await _dbContext.CourseBookings
            .Where(x => x.CourseSessionId == courseSessionId && x.Status != BookingStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var duplicateExists = existingBookings.Any(x =>
            ((x.StudentProfileId.HasValue && x.StudentProfileId == studentProfileId)
                || (!x.StudentProfileId.HasValue && x.Email == email && x.CustomerName == customerName))
            && (x.Status == BookingStatus.Confirmed || (x.Status == BookingStatus.Pending && x.ExpiresAt > DateTimeOffset.UtcNow)));

        if (duplicateExists)
        {
            throw new InvalidOperationException("This student already has an active booking for this course session.");
        }

        var occupiedSeats = await CountOccupiedSeatsAsync(courseSessionId, cancellationToken);
        if (occupiedSeats >= session.Capacity)
        {
            throw new InvalidOperationException("This course session is full and cannot accept more bookings.");
        }

        var booking = CourseBooking.CreatePendingReservation(
            session.Id,
            customerName,
            email,
            phone,
            studentProfileId);

        _dbContext.CourseBookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateBookingResult(
            booking.Id,
            booking.CourseSessionId,
            booking.Status,
            session.Capacity - (occupiedSeats + 1));
    }

    public async Task<CreateBookingResult> CreateDirectConfirmedBookingAsync(
        Guid courseSessionId,
        Guid studentProfileId,
        string customerName,
        string email,
        string phone,
        CancellationToken cancellationToken = default)
    {
        if (studentProfileId == Guid.Empty)
        {
            throw new ArgumentException("Student profile id cannot be empty.", nameof(studentProfileId));
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);
        EnsureSessionAcceptsBooking(session);

        var existingBookings = await _dbContext.CourseBookings
            .Where(x => x.CourseSessionId == courseSessionId && x.Status != BookingStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var duplicateExists = existingBookings.Any(x =>
            x.StudentProfileId == studentProfileId
            && (x.Status == BookingStatus.Confirmed || (x.Status == BookingStatus.Pending && x.ExpiresAt > DateTimeOffset.UtcNow)));

        if (duplicateExists)
        {
            throw new InvalidOperationException("This student already has an active booking for this course session.");
        }

        var occupiedSeats = await CountOccupiedSeatsAsync(courseSessionId, cancellationToken);
        if (occupiedSeats >= session.Capacity)
        {
            throw new InvalidOperationException("This course session is full and cannot accept more bookings.");
        }

        var booking = new CourseBooking(
            session.Id,
            customerName,
            email,
            phone,
            studentProfileId);

        booking.Confirm();
        _dbContext.CourseBookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateBookingResult(
            booking.Id,
            booking.CourseSessionId,
            booking.Status,
            session.Capacity - (occupiedSeats + 1));
    }

    public async Task<CreateBookingResult> ConfirmPendingReservationAsync(Guid bookingId, Guid studentProfileId, CancellationToken cancellationToken = default)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var booking = await _dbContext.CourseBookings
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken);

        if (booking is null)
        {
            throw new InvalidOperationException("Booking was not found.");
        }

        if (booking.StudentProfileId != studentProfileId)
        {
            throw new InvalidOperationException("This reservation is not owned by the authenticated student.");
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            throw new InvalidOperationException("Cancelled bookings cannot be confirmed.");
        }

        if (booking.Status != BookingStatus.Pending)
        {
            throw new InvalidOperationException("Only pending reservations can be confirmed.");
        }

        if (booking.ExpiresAt.HasValue && booking.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("An expired reservation cannot be confirmed.");
        }

        var session = await GetLockedSessionAsync(booking.CourseSessionId, cancellationToken);
        EnsureSessionAcceptsBooking(session);

        var occupiedSeats = await CountOccupiedSeatsAsync(booking.CourseSessionId, cancellationToken);
        if (occupiedSeats >= session.Capacity)
        {
            throw new InvalidOperationException("This course session is full and cannot accept more bookings.");
        }

        booking.Confirm();
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateBookingResult(
            booking.Id,
            booking.CourseSessionId,
            booking.Status,
            session.Capacity - occupiedSeats);
    }

    private async Task<CourseSession> GetLockedSessionAsync(Guid courseSessionId, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            var session = await _dbContext.CourseSessions
                .FromSqlRaw("SELECT * FROM CourseSessions WITH (UPDLOCK, HOLDLOCK) WHERE Id = {0}", courseSessionId)
                .SingleOrDefaultAsync(cancellationToken);

            if (session is null)
            {
                throw new InvalidOperationException("Course session was not found.");
            }

            return session;
        }

        var fallbackSession = await _dbContext.CourseSessions
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (fallbackSession is null)
        {
            throw new InvalidOperationException("Course session was not found.");
        }

        return fallbackSession;
    }

    private static void EnsureSessionAcceptsBooking(CourseSession session)
    {
        if (!session.IsPublished)
        {
            throw new InvalidOperationException("The course session is not published and cannot accept bookings.");
        }

        if (session.StartTime <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The course session has already started or passed and cannot accept new bookings.");
        }
    }

    private async Task<int> CountOccupiedSeatsAsync(Guid courseSessionId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var bookings = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => x.CourseSessionId == courseSessionId && x.Status != BookingStatus.Cancelled)
            .ToListAsync(cancellationToken);

        return bookings.Count(x => x.Status == BookingStatus.Confirmed || (x.Status == BookingStatus.Pending && (!x.ExpiresAt.HasValue || x.ExpiresAt > now)));
    }
}
