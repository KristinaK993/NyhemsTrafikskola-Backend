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

        if (session.IsCancelled || session.IsArchived)
        {
            return 0;
        }

        var occupiedSeats = await CountOccupiedSeatsAsync(courseSessionId, cancellationToken);
        return Math.Max(0, CourseSession.StandardCapacity - occupiedSeats);
    }

    public async Task<IReadOnlyCollection<CourseSessionAvailabilityDto>> GetPublishedFutureSessionsAsync(CourseType courseType, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var courses = await _dbContext.Courses
            .AsNoTracking()
            .Where(x => x.Type == courseType && x.IsActive)
            .ToListAsync(cancellationToken);

        var courseIds = courses.Select(x => x.Id).ToHashSet();

        var sessions = await _dbContext.CourseSessions
            .AsNoTracking()
            .Where(x => x.IsPublished && !x.IsCancelled && !x.IsArchived && courseIds.Contains(x.CourseId))
            .ToListAsync(cancellationToken);

        var matchingSessions = sessions
            .Where(x => x.StartTime > now)
            .ToList();

        var results = new List<CourseSessionAvailabilityDto>(matchingSessions.Count);

        foreach (var session in matchingSessions)
        {
            var course = courses.Single(x => x.Id == session.CourseId);
            var occupiedSeats = await CountOccupiedSeatsAsync(session.Id, cancellationToken);

            results.Add(new CourseSessionAvailabilityDto(
                session.Id,
                course.Id,
                course.Type,
                course.Title,
                course.Price,
                session.StartTime,
                session.EndTime,
                CourseSession.StandardCapacity,
                Math.Max(0, CourseSession.StandardCapacity - occupiedSeats)));
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
            throw new CourseBookingException(CourseBookingError.AlreadyBooked, "This student already has an active booking for this course session.");
        }

        var occupiedSeats = await CountOccupiedSeatsAsync(courseSessionId, cancellationToken);
        if (occupiedSeats >= CourseSession.StandardCapacity)
        {
            throw new CourseBookingException(CourseBookingError.SessionFull, "This course session is full and cannot accept more bookings.");
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
            Math.Max(0, CourseSession.StandardCapacity - (occupiedSeats + 1)));
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

        // Staff-created bookings may belong to a separate, unlinked profile for the same person, so the email is compared as well.
        var normalizedEmail = email.Trim();
        var duplicateExists = existingBookings.Any(x =>
            (x.StudentProfileId == studentProfileId || string.Equals(x.Email.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase))
            && (x.Status == BookingStatus.Confirmed || (x.Status == BookingStatus.Pending && x.ExpiresAt > DateTimeOffset.UtcNow)));

        if (duplicateExists)
        {
            throw new CourseBookingException(CourseBookingError.AlreadyBooked, "This student already has an active booking for this course session.");
        }

        var occupiedSeats = await CountOccupiedSeatsAsync(courseSessionId, cancellationToken);
        if (occupiedSeats >= CourseSession.StandardCapacity)
        {
            throw new CourseBookingException(CourseBookingError.SessionFull, "This course session is full and cannot accept more bookings.");
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
            Math.Max(0, CourseSession.StandardCapacity - (occupiedSeats + 1)));
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
        if (occupiedSeats > CourseSession.StandardCapacity)
        {
            throw new CourseBookingException(CourseBookingError.SessionFull, "This course session is full and cannot accept more bookings.");
        }

        booking.Confirm();
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateBookingResult(
            booking.Id,
            booking.CourseSessionId,
            booking.Status,
            Math.Max(0, CourseSession.StandardCapacity - occupiedSeats));
    }

    public async Task CancelStudentBookingAsync(Guid bookingId, IReadOnlyCollection<Guid> ownStudentProfileIds, CancellationToken cancellationToken = default)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var booking = await _dbContext.CourseBookings
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken);

        // Only bookings on the student's own linked profiles can be cancelled online; others are reported as not found.
        if (booking is null || booking.StudentProfileId is null || !ownStudentProfileIds.Contains(booking.StudentProfileId.Value))
        {
            throw new CourseBookingException(CourseBookingError.BookingNotFound, "Booking was not found.");
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            throw new CourseBookingException(CourseBookingError.BookingAlreadyCancelled, "The booking has already been cancelled.");
        }

        var session = await GetLockedSessionAsync(booking.CourseSessionId, cancellationToken);
        if (!session.AllowsOnlineCancellation(DateTimeOffset.UtcNow))
        {
            throw new CourseBookingException(CourseBookingError.CancellationDeadlinePassed, "The online cancellation deadline has passed.");
        }

        booking.Cancel();
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
                throw new CourseBookingException(CourseBookingError.SessionNotFound, "Course session was not found.");
            }

            return session;
        }

        var fallbackSession = await _dbContext.CourseSessions
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (fallbackSession is null)
        {
            throw new CourseBookingException(CourseBookingError.SessionNotFound, "Course session was not found.");
        }

        return fallbackSession;
    }

    private static void EnsureSessionAcceptsBooking(CourseSession session)
    {
        if (session.IsArchived)
        {
            throw new CourseBookingException(CourseBookingError.SessionArchived, "The course session has been archived and cannot accept bookings.");
        }

        if (session.IsCancelled)
        {
            throw new CourseBookingException(CourseBookingError.SessionCancelled, "The course session has been cancelled and cannot accept bookings.");
        }

        if (!session.IsPublished)
        {
            throw new CourseBookingException(CourseBookingError.SessionNotPublished, "The course session is not published and cannot accept bookings.");
        }

        if (session.StartTime <= DateTimeOffset.UtcNow)
        {
            throw new CourseBookingException(CourseBookingError.SessionStarted, "The course session has already started or passed and cannot accept new bookings.");
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
