using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Authorization;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Infrastructure;

namespace NyhemsTrafikskola.API.Controllers;

[ApiController]
[Route("api/course-sessions/{courseSessionId:guid}/bookings")]
[Authorize(Roles = ApplicationRoles.Student)]
public sealed class StudentCourseBookingsController : ControllerBase
{
    private readonly ICourseBookingService _courseBookingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    public StudentCourseBookingsController(
        ICourseBookingService courseBookingService,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext)
    {
        _courseBookingService = courseBookingService;
        _userManager = userManager;
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid courseSessionId,
        [FromBody] StudentBookingRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.TermsAccepted != true)
        {
            return Error(StatusCodes.Status400BadRequest, "TermsNotAccepted", "Du måste godkänna boknings- och avbokningsvillkoren för att kunna boka.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { code = "AuthenticationRequired", message = "Du behöver logga in för att boka." });
        }

        // The student is always identified from the signed-in account, never from the request body.
        var studentProfileId = await _dbContext.StudentProfiles
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (studentProfileId is null)
        {
            return Error(StatusCodes.Status409Conflict, "StudentProfileMissing", "Ditt elevkonto saknar elevprofil. Kontakta trafikskolan.");
        }

        var customerName = $"{user.FirstName} {user.LastName}".Trim();
        var email = user.Email?.Trim();
        var phone = user.PhoneNumber?.Trim();

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(email))
        {
            return Error(StatusCodes.Status400BadRequest, "ContactDetailsMissing", "Ditt konto saknar namn eller e-postadress. Kontakta trafikskolan.");
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            return Error(StatusCodes.Status400BadRequest, "PhoneNumberMissing", "Ditt konto saknar telefonnummer. Kontakta trafikskolan för att komplettera dina uppgifter.");
        }

        try
        {
            var result = await _courseBookingService.CreateDirectConfirmedBookingAsync(
                courseSessionId,
                studentProfileId.Value,
                customerName,
                email,
                phone,
                cancellationToken);

            return Created(
                $"/api/course-sessions/{courseSessionId}/bookings/{result.BookingId}",
                new StudentBookingResponse(result.BookingId, result.CourseSessionId, result.Status.ToString(), result.AvailableSeats));
        }
        catch (CourseBookingException exception)
        {
            return exception.Error switch
            {
                CourseBookingError.SessionNotFound => Error(StatusCodes.Status404NotFound, "SessionNotFound", "Kurstillfället hittades inte."),
                CourseBookingError.SessionArchived => Error(StatusCodes.Status409Conflict, "SessionArchived", "Kurstillfället är arkiverat och kan inte bokas."),
                CourseBookingError.SessionCancelled => Error(StatusCodes.Status409Conflict, "SessionCancelled", "Kurstillfället är inställt och kan inte bokas."),
                CourseBookingError.SessionNotPublished => Error(StatusCodes.Status409Conflict, "SessionNotPublished", "Kurstillfället är inte öppet för bokning."),
                CourseBookingError.SessionStarted => Error(StatusCodes.Status409Conflict, "SessionStarted", "Kurstillfället har redan startat eller passerat och kan inte bokas."),
                CourseBookingError.AlreadyBooked => Error(StatusCodes.Status409Conflict, "AlreadyBooked", "Du har redan en bokning på det här kurstillfället."),
                CourseBookingError.SessionFull => Error(StatusCodes.Status409Conflict, "SessionFull", "Kurstillfället är fullbokat."),
                _ => Error(StatusCodes.Status409Conflict, "BookingFailed", "Bokningen kunde inte genomföras."),
            };
        }
    }

    private ObjectResult Error(int statusCode, string code, string message) =>
        StatusCode(statusCode, new { code, message });

    public sealed record StudentBookingRequest(bool TermsAccepted);

    public sealed record StudentBookingResponse(Guid BookingId, Guid CourseSessionId, string Status, int AvailableSeats);
}
