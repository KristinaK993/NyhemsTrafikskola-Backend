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

    Task DeleteCourseSessionAsync(Guid courseSessionId, CancellationToken cancellationToken = default);
}
