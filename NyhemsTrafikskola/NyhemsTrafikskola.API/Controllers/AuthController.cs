using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Authorization;
using NyhemsTrafikskola.Application.Validation;
using NyhemsTrafikskola.Domain;
using NyhemsTrafikskola.Infrastructure;

namespace NyhemsTrafikskola.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IAntiforgery _antiforgery;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IAntiforgery antiforgery)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _dbContext = dbContext;
        _antiforgery = antiforgery;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Fyll i dina uppgifter för att skapa ett konto." });
        }

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        var phoneNumber = request.PhoneNumber?.Trim();
        var email = request.Email?.Trim();

        if (string.IsNullOrWhiteSpace(firstName))
        {
            return FieldError("firstName", "Fyll i ditt förnamn.");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            return FieldError("lastName", "Fyll i ditt efternamn.");
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return FieldError("phoneNumber", "Fyll i ditt telefonnummer.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return FieldError("email", "Fyll i din e-postadress.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return FieldError("password", "Fyll i ett lösenord.");
        }

        var validationError = ValidateRegistrationDetails(firstName, lastName, phoneNumber, email);
        if (validationError is not null)
        {
            return FieldError(validationError.Value.Field, validationError.Value.Message);
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return FieldError("confirmPassword", "Lösenorden matchar inte.");
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return FieldError("email", DuplicateEmailMessage, StatusCodes.Status409Conflict);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phoneNumber,
            IsActive = true,
        };

        // Account, role and profile are created together so a failure never leaves a half-created account behind.
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return IdentityError(createResult);
            }

            var addToRoleResult = await _userManager.AddToRoleAsync(user, ApplicationRoles.Student);
            if (!addToRoleResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return IdentityError(addToRoleResult);
            }

            var profile = StudentProfile.CreateSelfRegistration(firstName, lastName, email);
            profile.UserId = user.Id;

            _dbContext.StudentProfiles.Add(profile);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        IssueAntiforgeryToken();

        return Ok(new { message = "Registration successful." });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var user = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: true,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        IssueAntiforgeryToken();

        return Ok(new { message = "Login successful." });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized(new { message = "Authentication required." });
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToArray();
        IssueAntiforgeryToken();

        var studentProfileId = await _dbContext.StudentProfiles
            .Where(x => x.UserId == user.Id)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync();

        var instructorProfileId = await _dbContext.InstructorProfiles
            .Where(x => x.UserId == user.Id)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync();

        return Ok(new MeResponse
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            Roles = roles,
            StudentProfileId = studentProfileId,
            InstructorProfileId = instructorProfileId,
        });
    }

    public sealed record LoginRequest(string Email, string Password);

    public sealed record RegisterRequest(
        string FirstName,
        string LastName,
        string PhoneNumber,
        string Email,
        string Password,
        string ConfirmPassword);

    // Limits match the database columns for ApplicationUser, StudentProfile and CourseBooking.
    private const int MaxNameLength = 100;
    private const int MaxEmailLength = 200;

    private const string DuplicateEmailMessage = "Det finns redan ett konto med den här e-postadressen.";
    private const string InvalidEmailMessage = "Ange en giltig e-postadress.";

    private static (string Field, string Message)? ValidateRegistrationDetails(string firstName, string lastName, string phoneNumber, string email)
    {
        if (firstName.Length > MaxNameLength)
        {
            return ("firstName", $"Förnamnet får vara högst {MaxNameLength} tecken.");
        }

        if (lastName.Length > MaxNameLength)
        {
            return ("lastName", $"Efternamnet får vara högst {MaxNameLength} tecken.");
        }

        if (!SwedishPhoneNumber.IsValid(phoneNumber))
        {
            return ("phoneNumber", "Ange ett giltigt svenskt telefonnummer med högst 10 siffror, till exempel 070-123 45 67.");
        }

        if (email.Length > MaxEmailLength || !new EmailAddressAttribute().IsValid(email))
        {
            return ("email", InvalidEmailMessage);
        }

        return null;
    }

    // "field" tells the frontend which input the message belongs to, so it can be shown under that field.
    private ObjectResult FieldError(string? field, string message, int statusCode = StatusCodes.Status400BadRequest) =>
        StatusCode(statusCode, new { field, message });

    private IActionResult IdentityError(IdentityResult result)
    {
        var codes = result.Errors.Select(x => x.Code).ToArray();

        if (codes.Contains(nameof(IdentityErrorDescriber.DuplicateEmail)) || codes.Contains(nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return FieldError("email", DuplicateEmailMessage, StatusCodes.Status409Conflict);
        }

        if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
        {
            return FieldError("password", "Lösenordet måste vara minst 8 tecken och innehålla stor bokstav, liten bokstav och en siffra.");
        }

        if (codes.Contains(nameof(IdentityErrorDescriber.InvalidEmail)) || codes.Contains(nameof(IdentityErrorDescriber.InvalidUserName)))
        {
            return FieldError("email", InvalidEmailMessage);
        }

        return FieldError(null, "Kontot kunde inte skapas. Försök igen eller kontakta trafikskolan.");
    }

    private void IssueAntiforgeryToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers["X-XSRF-TOKEN"] = tokens.RequestToken;
    }

    public sealed record MeResponse
    {
        public string Id { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string PhoneNumber { get; init; } = string.Empty;
        public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
        public Guid? StudentProfileId { get; init; }
        public Guid? InstructorProfileId { get; init; }
    }
}
