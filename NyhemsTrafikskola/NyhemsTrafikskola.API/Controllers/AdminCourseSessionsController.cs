using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
    public async Task<ActionResult<IReadOnlyCollection<CourseSessionAdminResponse>>> GetAll([FromQuery] bool archived = false, CancellationToken cancellationToken = default)
    {
        var sessions = await _administrationService.GetCourseSessionSummariesAsync(archived, cancellationToken);
        return Ok(sessions.Select(CourseSessionAdminResponse.From));
    }

    [HttpGet("{courseSessionId:guid}")]
    public async Task<ActionResult<CourseSessionAdminDetailsResponse>> GetDetails(Guid courseSessionId, CancellationToken cancellationToken)
    {
        try
        {
            var details = await _administrationService.GetCourseSessionDetailsAsync(courseSessionId, cancellationToken);
            return Ok(CourseSessionAdminDetailsResponse.From(details));
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost("{courseSessionId:guid}/bookings")]
    public async Task<ActionResult<CourseSessionAdminDetailsResponse>> BookStudent(
        Guid courseSessionId,
        [FromBody] ManualBookingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var details = await _administrationService.BookStudentAsync(
                courseSessionId,
                request.FirstName ?? string.Empty,
                request.LastName ?? string.Empty,
                request.Phone ?? string.Empty,
                request.Email ?? string.Empty,
                CurrentUserId,
                cancellationToken);
            return Ok(CourseSessionAdminDetailsResponse.From(details));
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

    [HttpPost("{courseSessionId:guid}/bookings/{bookingId:guid}/cancel")]
    public async Task<ActionResult<CourseSessionAdminDetailsResponse>> CancelBooking(
        Guid courseSessionId,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        try
        {
            var details = await _administrationService.CancelBookingAsync(courseSessionId, bookingId, CurrentUserId, cancellationToken);
            return Ok(CourseSessionAdminDetailsResponse.From(details));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{courseSessionId:guid}/cancel")]
    public async Task<ActionResult<CourseSessionAdminDetailsResponse>> CancelCourseSession(Guid courseSessionId, CancellationToken cancellationToken)
    {
        try
        {
            var details = await _administrationService.CancelCourseSessionAsync(courseSessionId, CurrentUserId, cancellationToken);
            return Ok(CourseSessionAdminDetailsResponse.From(details));
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost("{courseSessionId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid courseSessionId, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationService.ArchiveCourseSessionAsync(courseSessionId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<CourseSessionAdminResponse>> Create(
        [FromBody] CreateCourseSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Capacity != CourseSession.StandardCapacity)
        {
            return BadRequest(new { message = "Riskettan Bil and Riskettan MC sessions must have a capacity of 16 participants." });
        }

        try
        {
            var session = await _administrationService.CreateCourseSessionAsync(
                request.CourseId,
                request.StartTime,
                request.EndTime,
                CourseSession.StandardCapacity,
                request.IsPublished,
                cancellationToken);

            return Created("/api/admin/course-sessions", await CreateCourseSessionAdminResponseAsync(session, cancellationToken));
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
        if (request.Capacity.HasValue && request.Capacity.Value != CourseSession.StandardCapacity)
        {
            return BadRequest(new { message = "Riskettan Bil and Riskettan MC sessions must have a capacity of 16 participants." });
        }

        try
        {
            var session = await _administrationService.UpdateCourseSessionAsync(
                courseSessionId,
                request.StartTime,
                request.EndTime,
                CourseSession.StandardCapacity,
                cancellationToken);

            return Ok(await CreateCourseSessionAdminResponseAsync(session, cancellationToken));
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

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<CourseSessionAdminResponse> CreateCourseSessionAdminResponseAsync(CourseSession session, CancellationToken cancellationToken)
    {
        var summaries = await _administrationService.GetCourseSessionSummariesAsync(session.IsArchived, cancellationToken);
        var summary = summaries.Single(x => x.Session.Id == session.Id);
        return CourseSessionAdminResponse.From(summary);
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

    public sealed record ManualBookingRequest(
        string? FirstName,
        string? LastName,
        string? Phone,
        string? Email);

    public sealed record CourseSessionAdminResponse(
        Guid Id,
        Guid CourseId,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        int Capacity,
        bool IsPublished,
        bool IsCancelled,
        bool IsArchived,
        [property: JsonPropertyName("activeBookingCount")] int ActiveBookingCount,
        [property: JsonPropertyName("availableSeats")] int AvailableSeats,
        int BookingHistoryCount)
    {
        public static CourseSessionAdminResponse From(CourseSessionAdminSummary summary) => new(
            summary.Session.Id,
            summary.Session.CourseId,
            summary.Session.StartTime,
            summary.Session.EndTime,
            CourseSession.StandardCapacity,
            summary.Session.IsPublished,
            summary.Session.IsCancelled,
            summary.Session.IsArchived,
            summary.ActiveBookingCount,
            summary.AvailableSeats,
            summary.BookingHistoryCount);
    }

    public sealed record CourseSessionAdminDetailsResponse(
        Guid CourseSessionId,
        int Capacity,
        int ActiveBookingCount,
        int AvailableSeats,
        bool IsCancelled,
        bool IsArchived,
        IReadOnlyCollection<CourseBookingAdminResponse> Bookings)
    {
        public static CourseSessionAdminDetailsResponse From(CourseSessionAdminDetails details) => new(
            details.CourseSessionId,
            details.Capacity,
            details.ActiveBookingCount,
            details.AvailableSeats,
            details.IsCancelled,
            details.IsArchived,
            details.Bookings.Select(CourseBookingAdminResponse.From).ToList());
    }

    public sealed record CourseBookingAdminResponse(
        Guid Id,
        string CustomerName,
        string? FirstName,
        string? LastName,
        string Email,
        string Phone,
        DateTimeOffset CreatedAt,
        string Status,
        bool CountsTowardCapacity,
        bool BookedOnline,
        string? BookedByName,
        DateTimeOffset? TermsAcceptedAt,
        string? TermsVersion,
        DateTimeOffset? CancelledAt,
        string? CancellationReason,
        string? CancelledByName,
        bool IsLateCancellation)
    {
        public static CourseBookingAdminResponse From(CourseBookingAdminDetails booking) => new(
            booking.Id,
            booking.CustomerName,
            booking.FirstName,
            booking.LastName,
            booking.Email,
            booking.Phone,
            booking.CreatedAt,
            booking.Status,
            booking.CountsTowardCapacity,
            booking.BookedOnline,
            booking.BookedByName,
            booking.TermsAcceptedAt,
            booking.TermsVersion,
            booking.CancelledAt,
            booking.CancellationReason,
            booking.CancelledByName,
            booking.IsLateCancellation);
    }
}
