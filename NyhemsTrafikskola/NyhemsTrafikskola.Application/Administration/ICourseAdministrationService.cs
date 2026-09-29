using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Application.Administration;

public interface ICourseAdministrationService
{
    Task<IReadOnlyCollection<Course>> GetCoursesAsync(CancellationToken cancellationToken = default);
}
