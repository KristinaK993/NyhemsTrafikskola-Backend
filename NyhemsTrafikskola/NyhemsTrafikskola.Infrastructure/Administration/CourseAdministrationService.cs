using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Administration;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Infrastructure;

public sealed class CourseAdministrationService : ICourseAdministrationService
{
    private readonly ApplicationDbContext _dbContext;

    public CourseAdministrationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<Course>> GetCoursesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .OrderBy(course => course.Type)
            .ThenBy(course => course.Title)
            .ThenBy(course => course.Id)
            .ToListAsync(cancellationToken);
    }
}
