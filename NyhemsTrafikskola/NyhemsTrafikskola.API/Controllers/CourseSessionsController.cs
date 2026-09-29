using Microsoft.AspNetCore.Mvc;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.API.Controllers;

[ApiController]
[Route("api/course-sessions")]
public class CourseSessionsController : ControllerBase
{
    private readonly ICourseBookingService _courseBookingService;

    public CourseSessionsController(ICourseBookingService courseBookingService)
    {
        _courseBookingService = courseBookingService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CourseSessionResponseDto>>> Get(
        [FromQuery] string courseType,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CourseType>(courseType, true, out var parsedCourseType))
        {
            return BadRequest("Invalid course type.");
        }

        var sessions = await _courseBookingService.GetPublishedFutureSessionsAsync(parsedCourseType, cancellationToken);
        var response = sessions.Select(x => new CourseSessionResponseDto(
            x.CourseSessionId,
            x.CourseType,
            x.CourseTitle,
            x.StartTime,
            x.EndTime,
            x.Price,
            x.Capacity,
            x.AvailableSeats));

        return Ok(response);
    }
}

public sealed record CourseSessionResponseDto(
    Guid CourseSessionId,
    CourseType CourseType,
    string CourseTitle,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    decimal Price,
    int TotalSeats,
    int AvailableSeats);
