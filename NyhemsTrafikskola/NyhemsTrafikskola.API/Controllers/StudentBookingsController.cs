using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Authorization;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Infrastructure;

namespace NyhemsTrafikskola.API.Controllers;

[ApiController]
[Route("api/student/bookings")]
[Authorize(Roles = ApplicationRoles.Student)]
public sealed class StudentBookingsController : ControllerBase
{
    private readonly IStudentBookingQueryService _studentBookingQueryService;
    private readonly ICourseBookingService _courseBookingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    public StudentBookingsController(
        IStudentBookingQueryService studentBookingQueryService,
        ICourseBookingService courseBookingService,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext)
    {
        _studentBookingQueryService = studentBookingQueryService;
        _courseBookingService = courseBookingService;
        _userManager = userManager;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<StudentBookingResponse>>> GetUpcoming(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { code = "AuthenticationRequired", message = "Du behöver logga in för att se dina bokningar." });
        }

        // Bookings are looked up from the signed-in account only; the response deliberately omits names and contact details.
        var studentProfileIds = await _dbContext.StudentProfiles
            .Where(x => x.UserId == user.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var bookings = await _studentBookingQueryService.GetUpcomingBookingsAsync(studentProfileIds, user.Email, cancellationToken);

        return Ok(bookings.Select(x => new StudentBookingResponse(
            x.BookingId,
            x.CourseSessionId,
            x.CourseType.ToString(),
            x.CourseTitle,
            x.Price,
            x.StartTime,
            x.EndTime,
            x.Status.ToString(),
            x.BookedAt,
            x.OnlineCancellationDeadline,
            x.OnlineCancellation.ToString())));
    }

    [HttpPost("{bookingId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid bookingId, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { code = "AuthenticationRequired", message = "Du behöver logga in för att avboka." });
        }

        var ownStudentProfileIds = await _dbContext.StudentProfiles
            .Where(x => x.UserId == user.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        try
        {
            await _courseBookingService.CancelStudentBookingAsync(bookingId, ownStudentProfileIds, cancellationToken);
            return Ok(new { bookingId, status = "Cancelled" });
        }
        catch (CourseBookingException exception)
        {
            return exception.Error switch
            {
                CourseBookingError.BookingNotFound => Error(StatusCodes.Status404NotFound, "BookingNotFound", "Bokningen hittades inte eller kan inte avbokas online. Kontakta trafikskolan via telefon."),
                CourseBookingError.BookingAlreadyCancelled => Error(StatusCodes.Status409Conflict, "BookingAlreadyCancelled", "Bokningen är redan avbokad."),
                CourseBookingError.CancellationDeadlinePassed => Error(StatusCodes.Status409Conflict, "CancellationDeadlinePassed", "Tiden för avbokning online har passerat. Avbokning sker nu endast via telefon."),
                CourseBookingError.SessionNotFound => Error(StatusCodes.Status404NotFound, "BookingNotFound", "Bokningen hittades inte eller kan inte avbokas online. Kontakta trafikskolan via telefon."),
                _ => Error(StatusCodes.Status409Conflict, "CancellationFailed", "Bokningen kunde inte avbokas online. Kontakta trafikskolan via telefon."),
            };
        }
    }

    private ObjectResult Error(int statusCode, string code, string message) =>
        StatusCode(statusCode, new { code, message });

    public sealed record StudentBookingResponse(
        Guid BookingId,
        Guid CourseSessionId,
        string CourseType,
        string CourseTitle,
        decimal Price,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        string Status,
        DateTimeOffset BookedAt,
        DateTimeOffset OnlineCancellationDeadline,
        string OnlineCancellation);
}
