using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Infrastructure;

public sealed class StudentBookingQueryService : IStudentBookingQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public StudentBookingQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<StudentBookingDto>> GetUpcomingBookingsAsync(
        IReadOnlyCollection<Guid> studentProfileIds,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var profileIds = studentProfileIds.ToArray();
        var normalizedEmail = email?.Trim().ToUpperInvariant();

        if (profileIds.Length == 0 && string.IsNullOrEmpty(normalizedEmail))
        {
            return Array.Empty<StudentBookingDto>();
        }

        // Staff-created bookings may sit on a separate profile without an account, so they are matched on email as well.
        var bookings = await _dbContext.CourseBookings
            .AsNoTracking()
            .Where(x => x.Status == BookingStatus.Confirmed
                && ((x.StudentProfileId != null && profileIds.Contains(x.StudentProfileId.Value))
                    || (normalizedEmail != null && x.Email.ToUpper() == normalizedEmail)))
            .ToListAsync(cancellationToken);

        if (bookings.Count == 0)
        {
            return Array.Empty<StudentBookingDto>();
        }

        var sessionIds = bookings.Select(x => x.CourseSessionId).Distinct().ToArray();
        var sessions = await _dbContext.CourseSessions
            .AsNoTracking()
            .Where(x => sessionIds.Contains(x.Id) && !x.IsCancelled)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var courseIds = sessions.Values.Select(x => x.CourseId).Distinct().ToArray();
        var courses = await _dbContext.Courses
            .AsNoTracking()
            .Where(x => courseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // DateTimeOffset filtering and ordering happen in memory, as elsewhere in the booking services, to stay provider-independent.
        return bookings
            .Where(x => sessions.ContainsKey(x.CourseSessionId))
            .Select(x => (Booking: x, Session: sessions[x.CourseSessionId]))
            .Where(x => x.Session.EndTime > now && courses.ContainsKey(x.Session.CourseId))
            .OrderBy(x => x.Session.StartTime)
            .Select(x =>
            {
                var course = courses[x.Session.CourseId];
                var ownsBooking = x.Booking.StudentProfileId.HasValue && profileIds.Contains(x.Booking.StudentProfileId.Value);
                var onlineCancellation = !x.Session.AllowsOnlineCancellation(now)
                    ? OnlineCancellationAvailability.DeadlinePassed
                    : ownsBooking ? OnlineCancellationAvailability.Allowed : OnlineCancellationAvailability.ContactSchool;

                return new StudentBookingDto(
                    x.Booking.Id,
                    x.Session.Id,
                    course.Type,
                    course.Title,
                    course.Price,
                    x.Session.StartTime,
                    x.Session.EndTime,
                    x.Booking.Status,
                    x.Booking.CreatedAt,
                    x.Session.GetOnlineCancellationDeadline(),
                    onlineCancellation);
            })
            .ToList();
    }
}
