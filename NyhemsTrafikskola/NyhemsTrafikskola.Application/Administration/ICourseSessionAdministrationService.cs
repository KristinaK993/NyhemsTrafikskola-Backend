using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Application.Administration;

public interface ICourseSessionAdministrationService
{
    Task<CourseSession> CreateCourseSessionAsync(
        Guid courseId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int capacity,
        bool isPublished = false,
        CancellationToken cancellationToken = default);

    Task<CourseSession> UpdateCourseSessionAsync(
        Guid courseSessionId,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int? capacity = null,
        CancellationToken cancellationToken = default);

    Task PublishCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default);
    Task UnpublishCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CourseSession>> GetAllCourseSessionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CourseSessionAdminSummary>> GetCourseSessionSummariesAsync(bool archived, CancellationToken cancellationToken = default);

    Task<CourseSessionAdminDetails> GetCourseSessionDetailsAsync(Guid courseSessionId, CancellationToken cancellationToken = default);
    Task<CourseSessionAdminDetails> BookStudentAsync(
        Guid courseSessionId,
        string firstName,
        string lastName,
        string phone,
        string email,
        CancellationToken cancellationToken = default);
    Task<CourseSessionAdminDetails> CancelBookingAsync(Guid courseSessionId, Guid bookingId, CancellationToken cancellationToken = default);
    Task<CourseSessionAdminDetails> CancelCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default);
    Task ArchiveCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default);

    Task DeleteCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default);
}

public sealed record CourseSessionAdminDetails(
    Guid CourseSessionId,
    int Capacity,
    int ActiveBookingCount,
    int AvailableSeats,
    bool IsCancelled,
    bool IsArchived,
    IReadOnlyCollection<CourseBookingAdminDetails> Bookings);

public sealed record CourseSessionAdminSummary(
    CourseSession Session,
    int BookingHistoryCount,
    int ActiveBookingCount,
    int AvailableSeats);

public sealed record CourseBookingAdminDetails(
    Guid Id,
    string CustomerName,
    string? FirstName,
    string? LastName,
    string Email,
    string Phone,
    DateTimeOffset CreatedAt,
    string Status,
    bool CountsTowardCapacity);
