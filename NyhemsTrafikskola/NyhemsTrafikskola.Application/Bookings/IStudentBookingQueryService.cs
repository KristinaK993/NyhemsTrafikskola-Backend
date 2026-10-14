using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Application.Bookings;

public interface IStudentBookingQueryService
{
    Task<IReadOnlyCollection<StudentBookingDto>> GetUpcomingBookingsAsync(
        IReadOnlyCollection<Guid> studentProfileIds,
        string? email,
        CancellationToken cancellationToken = default);
}

public sealed record StudentBookingDto(
    Guid BookingId,
    Guid CourseSessionId,
    CourseType CourseType,
    string CourseTitle,
    decimal Price,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    BookingStatus Status,
    DateTimeOffset BookedAt,
    DateTimeOffset OnlineCancellationDeadline,
    OnlineCancellationAvailability OnlineCancellation);

public enum OnlineCancellationAvailability
{
    Allowed,
    DeadlinePassed,
    ContactSchool,
}
