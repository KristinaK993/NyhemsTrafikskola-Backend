using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyhemsTrafikskola.Application.Administration;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.API.Controllers;

[ApiController]
[Route("api/admin/course-sessions")]
[Authorize(Policy = "RequireInstructorOrAdmin")]
public sealed class AdminCourseSessionsController : ControllerBase
{
    private readonly ICourseSessionAdministrationService _administrationService;

    public AdminCourseSessionsController(ICourseSessionAdministrationService administrationService)
    {
        _administrationService = administrationService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CourseSessionAdminResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var sessions = await _administrationService.GetAllCourseSessionsAsync(cancellationToken);
        return Ok(sessions.Select(CourseSessionAdminResponse.From));
    }

    [HttpPost]
    public async Task<ActionResult<CourseSessionAdminResponse>> Create(
        [FromBody] CreateCourseSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _administrationService.CreateCourseSessionAsync(
                request.CourseId,
                request.StartTime,
                request.EndTime,
                request.Capacity,
                request.IsPublished,
                cancellationToken);

            return Created("/api/admin/course-sessions", CourseSessionAdminResponse.From(session));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{courseSessionId:guid}")]
    public async Task<ActionResult<CourseSessionAdminResponse>> Update(
        Guid courseSessionId,
        [FromBody] UpdateCourseSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _administrationService.UpdateCourseSessionAsync(
                courseSessionId,
                request.StartTime,
                request.EndTime,
                request.Capacity,
                cancellationToken);

            return Ok(CourseSessionAdminResponse.From(session));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{courseSessionId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid courseSessionId, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationService.PublishCourseSessionAsync(courseSessionId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{courseSessionId:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid courseSessionId, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationService.UnpublishCourseSessionAsync(courseSessionId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpDelete("{courseSessionId:guid}")]
    public async Task<IActionResult> Delete(Guid courseSessionId, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationService.DeleteCourseSessionAsync(courseSessionId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    public sealed record CreateCourseSessionRequest(
        Guid CourseId,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        int Capacity,
        bool IsPublished = false);

    public sealed record UpdateCourseSessionRequest(
        DateTimeOffset? StartTime,
        DateTimeOffset? EndTime,
        int? Capacity);

    public sealed record CourseSessionAdminResponse(
        Guid Id,
        Guid CourseId,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        int Capacity,
        bool IsPublished)
    {
        public static CourseSessionAdminResponse From(CourseSession session) => new(
            session.Id,
            session.CourseId,
            session.StartTime,
            session.EndTime,
            session.Capacity,
            session.IsPublished);
    }
}
