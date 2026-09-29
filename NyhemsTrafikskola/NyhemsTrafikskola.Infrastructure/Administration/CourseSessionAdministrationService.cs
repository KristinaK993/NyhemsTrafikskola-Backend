using Microsoft.EntityFrameworkCore;
using System.Data;
using NyhemsTrafikskola.Application.Administration;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Infrastructure;

public sealed class CourseSessionAdministrationService : ICourseSessionAdministrationService
{
    private readonly ApplicationDbContext _dbContext;

    public CourseSessionAdministrationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CourseSession> CreateCourseSessionAsync(
        Guid courseId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int capacity,
        bool isPublished = false,
        CancellationToken cancellationToken = default)
    {
        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Course id cannot be empty.", nameof(courseId));
        }

        var course = await _dbContext.Courses
            .SingleOrDefaultAsync(x => x.Id == courseId, cancellationToken);

        if (course is null)
        {
            throw new InvalidOperationException("Course was not found.");
        }

        if (!course.IsActive)
        {
            throw new InvalidOperationException("Only active courses can have sessions created.");
        }

        var session = new CourseSession(courseId, startTime, endTime, CourseSession.StandardCapacity, isPublished);
        _dbContext.CourseSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<CourseSession> UpdateCourseSessionAsync(
        Guid courseSessionId,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int? capacity = null,
        CancellationToken cancellationToken = default)
    {
        if (!startTime.HasValue && !endTime.HasValue && !capacity.HasValue)
        {
            throw new ArgumentException("At least one value must be supplied to update a course session.", nameof(courseSessionId));
        }

        var session = await _dbContext.CourseSessions
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException("Course session was not found.");
        }

        if (session.IsArchived)
        {
            throw new InvalidOperationException("An archived course session cannot be changed.");
        }

        if (capacity.HasValue && capacity.Value != CourseSession.StandardCapacity)
        {
            throw new InvalidOperationException($"Risketta capacity is fixed at {CourseSession.StandardCapacity} participants.");
        }

        var proposedStart = startTime ?? session.StartTime;
        var proposedEnd = endTime ?? session.EndTime;

        if (proposedEnd <= proposedStart)
        {
            throw new ArgumentException("End time must be later than start time.", nameof(endTime));
        }

        await EnsureCapacityDoesNotDropBelowActiveBookingsAsync(courseSessionId, CourseSession.StandardCapacity, cancellationToken);

        if (startTime.HasValue || endTime.HasValue)
        {
            session.UpdateSchedule(proposedStart, proposedEnd);
        }

        session.UpdateCapacity(CourseSession.StandardCapacity);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task PublishCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CourseSessions
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException("Course session was not found.");
        }

        await EnsureCapacityDoesNotDropBelowActiveBookingsAsync(courseSessionId, CourseSession.StandardCapacity, cancellationToken);
        session.UpdateCapacity(CourseSession.StandardCapacity);
        session.Publish();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnpublishCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CourseSessions
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException("Course session was not found.");
        }

        session.Unpublish();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CourseSession>> GetAllCourseSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.CourseSessions
            .AsNoTracking()
            .Where(x => !x.IsArchived)
            .ToListAsync(cancellationToken);

        return sessions
            .OrderBy(x => x.StartTime)
            .ToList();
    }

    public async Task<IReadOnlyCollection<CourseSessionAdminSummary>> GetCourseSessionSummariesAsync(bool archived, CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.CourseSessions
            .AsNoTracking()
            .Where(x => x.IsArchived == archived)
            .ToListAsync(cancellationToken);
        var sessionIds = sessions.Select(x => x.Id).ToArray();
        var historyCounts = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => sessionIds.Contains(x.CourseSessionId))
            .GroupBy(x => x.CourseSessionId)
            .Select(group => new { CourseSessionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.CourseSessionId, x => x.Count, cancellationToken);

        return sessions
            .OrderBy(x => x.StartTime)
            .Select(x => new CourseSessionAdminSummary(x, historyCounts.GetValueOrDefault(x.Id)))
            .ToList();
    }

    public async Task<CourseSessionAdminDetails> GetCourseSessionDetailsAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CourseSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException("Course session was not found.");
        }

        var bookings = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => x.CourseSessionId == courseSessionId)
            .ToListAsync(cancellationToken);
        bookings = bookings.OrderBy(x => x.CreatedAt).ToList();

        return await CreateAdminDetailsAsync(session, bookings, cancellationToken);
    }

    public async Task<CourseSessionAdminDetails> CancelBookingAsync(Guid courseSessionId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);
        var booking = await _dbContext.CourseBookings
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken);

        if (booking is null || booking.CourseSessionId != courseSessionId)
        {
            throw new InvalidOperationException("Booking was not found for this course session.");
        }

        booking.Cancel();
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var bookings = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => x.CourseSessionId == courseSessionId)
            .ToListAsync(cancellationToken);
        bookings = bookings.OrderBy(x => x.CreatedAt).ToList();
        return await CreateAdminDetailsAsync(session, bookings, cancellationToken);
    }

    public async Task<CourseSessionAdminDetails> CancelCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);
        var bookings = await _dbContext.CourseBookings
            .Where(x => x.CourseSessionId == courseSessionId)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var booking in bookings.Where(x => x.CountsTowardCapacity(now)))
        {
            booking.Cancel();
        }

        session.Cancel();
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await CreateAdminDetailsAsync(session, bookings, cancellationToken);
    }

    public async Task ArchiveCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);

        if (session.IsArchived)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var hasBookingHistory = await _dbContext.CourseBookings
            .AnyAsync(x => x.CourseSessionId == courseSessionId, cancellationToken);

        if (!hasBookingHistory)
        {
            throw new InvalidOperationException("This course session has no booking history and cannot be archived. Delete it permanently instead.");
        }

        session.Archive();
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CourseSessions
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException("Course session was not found.");
        }

        var hasBookingHistory = await _dbContext.CourseBookings
            .AnyAsync(x => x.CourseSessionId == courseSessionId, cancellationToken);

        if (hasBookingHistory)
        {
            throw new InvalidOperationException("Cannot delete a course session that has booking history.");
        }

        _dbContext.CourseSessions.Remove(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCapacityDoesNotDropBelowActiveBookingsAsync(Guid courseSessionId, int proposedCapacity, CancellationToken cancellationToken)
    {
        if (proposedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(proposedCapacity), "Capacity must be greater than zero.");
        }

        var activeBookings = await GetActiveBookingCountAsync(courseSessionId, cancellationToken);
        if (proposedCapacity < activeBookings)
        {
            throw new InvalidOperationException($"Capacity cannot be reduced below the current active booking count ({activeBookings}).");
        }
    }

    private async Task<CourseSession> GetLockedSessionAsync(Guid courseSessionId, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            var session = await _dbContext.CourseSessions
                .FromSqlRaw("SELECT * FROM CourseSessions WITH (UPDLOCK, HOLDLOCK) WHERE Id = {0}", courseSessionId)
                .SingleOrDefaultAsync(cancellationToken);

            return session ?? throw new InvalidOperationException("Course session was not found.");
        }

        var fallbackSession = await _dbContext.CourseSessions
            .SingleOrDefaultAsync(x => x.Id == courseSessionId, cancellationToken);

        return fallbackSession ?? throw new InvalidOperationException("Course session was not found.");
    }

    private async Task<CourseSessionAdminDetails> CreateAdminDetailsAsync(CourseSession session, IReadOnlyCollection<CourseBooking> bookings, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeBookings = bookings.Count(x => x.CountsTowardCapacity(now));
        var studentProfileIds = bookings
            .Where(x => x.StudentProfileId.HasValue)
            .Select(x => x.StudentProfileId!.Value)
            .Distinct()
            .ToArray();
        var profiles = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(x => studentProfileIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return new CourseSessionAdminDetails(
            session.Id,
            CourseSession.StandardCapacity,
            activeBookings,
            session.IsCancelled || session.IsArchived ? 0 : Math.Max(0, CourseSession.StandardCapacity - activeBookings),
            session.IsCancelled,
            session.IsArchived,
            bookings.Select(x => new CourseBookingAdminDetails(
                x.Id,
                x.CustomerName,
                x.StudentProfileId.HasValue && profiles.TryGetValue(x.StudentProfileId.Value, out var profile) ? profile.FirstName : null,
                x.StudentProfileId.HasValue && profiles.TryGetValue(x.StudentProfileId.Value, out profile) ? profile.LastName : null,
                x.Email,
                x.Phone,
                x.CreatedAt,
                x.Status.ToString(),
                x.CountsTowardCapacity(now))).ToList());
    }

    private async Task<int> GetActiveBookingCountAsync(Guid courseSessionId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var bookings = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => x.CourseSessionId == courseSessionId && x.Status != BookingStatus.Cancelled)
            .ToListAsync(cancellationToken);

        return bookings.Count(x => x.CountsTowardCapacity(now));
    }
}
