using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Data;
using NyhemsTrafikskola.Application.Administration;
using NyhemsTrafikskola.Application.Validation;
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
        var now = DateTimeOffset.UtcNow;
        var bookingStatuses = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => sessionIds.Contains(x.CourseSessionId))
            .Select(x => new { x.CourseSessionId, x.Status, x.ExpiresAt })
            .ToListAsync(cancellationToken);
        var bookingCounts = bookingStatuses
            .GroupBy(x => x.CourseSessionId)
            .ToDictionary(
                group => group.Key,
                group => (
                    History: group.Count(),
                    Active: group.Count(x => x.Status == BookingStatus.Confirmed
                        || (x.Status == BookingStatus.Pending && (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now)))));

        return sessions
            .OrderBy(x => x.StartTime)
            .Select(session =>
            {
                var counts = bookingCounts.GetValueOrDefault(session.Id);
                var activeBookingCount = counts.Active;
                var availableSeats = session.IsCancelled || session.IsArchived
                    ? 0
                    : Math.Max(0, CourseSession.StandardCapacity - activeBookingCount);
                return new CourseSessionAdminSummary(session, counts.History, activeBookingCount, availableSeats);
            })
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

    public async Task<CourseSessionAdminDetails> BookStudentAsync(
        Guid courseSessionId,
        string firstName,
        string lastName,
        string phone,
        string email,
        string? bookedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        firstName = ValidateManualBookingText(firstName, "first name", 100);
        lastName = ValidateManualBookingText(lastName, "last name", 100);
        phone = ValidateManualBookingText(phone, "phone number", 50);
        email = ValidateManualBookingText(email, "email address", 200);

        if (!new EmailAddressAttribute().IsValid(email))
        {
            throw new ArgumentException("Enter a valid email address.", nameof(email));
        }

        if (!SwedishPhoneNumber.IsValid(phone))
        {
            throw new ArgumentException("Enter a valid phone number.", nameof(phone));
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);

        if (session.IsCancelled)
        {
            throw new InvalidOperationException("A cancelled course session cannot accept bookings.");
        }

        if (session.IsArchived)
        {
            throw new InvalidOperationException("An archived course session cannot accept bookings.");
        }

        var normalizedEmail = email.ToUpperInvariant();
        var existingStudentUserId = await (
            from user in _dbContext.Users
            join userRole in _dbContext.UserRoles on user.Id equals userRole.UserId
            join role in _dbContext.Roles on userRole.RoleId equals role.Id
            where user.NormalizedEmail == normalizedEmail && role.NormalizedName == "STUDENT"
            select user.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var profile = await _dbContext.StudentProfiles
            .SingleOrDefaultAsync(x => x.Email.ToUpper() == normalizedEmail, cancellationToken);

        if (profile is null && existingStudentUserId is not null)
        {
            profile = await _dbContext.StudentProfiles
                .SingleOrDefaultAsync(x => x.UserId == existingStudentUserId, cancellationToken);
        }

        var shouldCreateProfile = profile is null;
        profile ??= StudentProfile.CreateForStaffRegistration(firstName, lastName, email);

        var existingBookings = await _dbContext.CourseBookings
            .Where(x => x.CourseSessionId == courseSessionId)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existingBookings.Any(x => x.CountsTowardCapacity(now)
            && (x.StudentProfileId == profile.Id || string.Equals(x.Email.Trim(), email, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("This student already has an active booking for this course session.");
        }

        var activeBookings = existingBookings.Count(x => x.CountsTowardCapacity(now));
        if (activeBookings >= CourseSession.StandardCapacity)
        {
            throw new InvalidOperationException("This course session is full and cannot accept more bookings.");
        }

        if (shouldCreateProfile)
        {
            profile.UserId = existingStudentUserId;
            _dbContext.StudentProfiles.Add(profile);
        }
        else if (profile.UserId is null && existingStudentUserId is not null)
        {
            profile.UserId = existingStudentUserId;
        }

        var booking = new CourseBooking(
            session.Id,
            $"{firstName} {lastName}",
            email,
            phone,
            profile.Id);
        booking.RecordCreatedBy(bookedByUserId);
        booking.Confirm();
        _dbContext.CourseBookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        existingBookings.Add(booking);
        await transaction.CommitAsync(cancellationToken);

        return await CreateAdminDetailsAsync(session, existingBookings, cancellationToken);
    }

    public async Task<CourseSessionAdminDetails> CancelBookingAsync(Guid courseSessionId, Guid bookingId, string? cancelledByUserId = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);
        var booking = await _dbContext.CourseBookings
            .SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken);

        if (booking is null || booking.CourseSessionId != courseSessionId)
        {
            throw new InvalidOperationException("Booking was not found for this course session.");
        }

        booking.Cancel(CancellationReason.Staff, cancelledByUserId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var bookings = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => x.CourseSessionId == courseSessionId)
            .ToListAsync(cancellationToken);
        bookings = bookings.OrderBy(x => x.CreatedAt).ToList();
        return await CreateAdminDetailsAsync(session, bookings, cancellationToken);
    }

    public async Task<CourseSessionAdminDetails> CancelCourseSessionAsync(Guid courseSessionId, string? cancelledByUserId = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await GetLockedSessionAsync(courseSessionId, cancellationToken);
        var bookings = await _dbContext.CourseBookings
            .Where(x => x.CourseSessionId == courseSessionId)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var booking in bookings.Where(x => x.CountsTowardCapacity(now)))
        {
            booking.Cancel(CancellationReason.SessionCancelled, cancelledByUserId, now);
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
        var actorIds = bookings
            .SelectMany(x => new[] { x.CreatedByUserId, x.CancelledByUserId })
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct()
            .ToArray();
        var actorNames = await _dbContext.Users
            .AsNoTracking()
            .Where(x => actorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim(), cancellationToken);

        string? ActorName(string? userId) =>
            userId is not null && actorNames.TryGetValue(userId, out var name) ? name : null;

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
                x.CountsTowardCapacity(now),
                x.TermsAcceptedAt.HasValue,
                ActorName(x.CreatedByUserId),
                x.TermsAcceptedAt,
                x.TermsVersion,
                x.CancelledAt,
                x.CancellationReason?.ToString(),
                ActorName(x.CancelledByUserId),
                x.IsLateCancellation(session.StartTime))).ToList());
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

    private static string ValidateManualBookingText(string? value, string fieldName, int maxLength)
    {
        var normalizedValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            throw new ArgumentException($"Enter the student's {fieldName}.", fieldName);
        }

        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException($"The student's {fieldName} must be {maxLength} characters or fewer.", fieldName);
        }

        return normalizedValue;
    }
}
