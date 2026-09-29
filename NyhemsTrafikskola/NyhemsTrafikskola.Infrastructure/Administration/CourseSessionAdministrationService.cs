using Microsoft.EntityFrameworkCore;
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

        var session = new CourseSession(courseId, startTime, endTime, capacity, isPublished);
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

        var proposedStart = startTime ?? session.StartTime;
        var proposedEnd = endTime ?? session.EndTime;

        if (proposedEnd <= proposedStart)
        {
            throw new ArgumentException("End time must be later than start time.", nameof(endTime));
        }

        if (capacity.HasValue)
        {
            await EnsureCapacityDoesNotDropBelowActiveBookingsAsync(courseSessionId, capacity.Value, cancellationToken);
        }

        if (startTime.HasValue || endTime.HasValue)
        {
            session.UpdateSchedule(proposedStart, proposedEnd);
        }

        if (capacity.HasValue)
        {
            session.UpdateCapacity(capacity.Value);
        }

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
            .ToListAsync(cancellationToken);

        return sessions
            .OrderBy(x => x.StartTime)
            .ToList();
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
