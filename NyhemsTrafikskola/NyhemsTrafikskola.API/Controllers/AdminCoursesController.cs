using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyhemsTrafikskola.Application.Administration;

namespace NyhemsTrafikskola.API.Controllers;

[ApiController]
[Route("api/admin/courses")]
[Authorize(Policy = "RequireInstructorOrAdmin")]
public sealed class AdminCoursesController : ControllerBase
{
    private readonly ICourseAdministrationService _courseAdministrationService;

    public AdminCoursesController(ICourseAdministrationService courseAdministrationService)
    {
        _courseAdministrationService = courseAdministrationService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<AdminCourseResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var courses = await _courseAdministrationService.GetCoursesAsync(cancellationToken);
        return Ok(courses.Select(course => new AdminCourseResponse(
            course.Id,
            course.Type.ToString(),
            course.Title,
            course.Price,
            course.IsActive)));
    }

    public sealed record AdminCourseResponse(Guid Id, string Type, string Title, decimal Price, bool IsActive);
}
