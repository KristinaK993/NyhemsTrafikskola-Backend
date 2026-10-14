using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Authorization;
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
            return BadRequest(new { message = "Registration details are required." });
        }

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        var phoneNumber = request.PhoneNumber?.Trim();
        var email = request.Email?.Trim();

        if (string.IsNullOrWhiteSpace(firstName)
            || string.IsNullOrWhiteSpace(lastName)
            || string.IsNullOrWhiteSpace(phoneNumber)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "First name, last name, phone number, email and password are required." });
        }

        if (!new EmailAddressAttribute().IsValid(email))
        {
            return BadRequest(new { message = "A valid email address is required." });
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Passwords do not match." });
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return Conflict(new { message = "An account with this email already exists." });
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

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errorMessage = createResult.Errors.FirstOrDefault()?.Description ?? "The account could not be created.";
            return BadRequest(new { message = errorMessage });
        }

        var addToRoleResult = await _userManager.AddToRoleAsync(user, ApplicationRoles.Student);
        if (!addToRoleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            var errorMessage = addToRoleResult.Errors.FirstOrDefault()?.Description ?? "The student role could not be assigned.";
            return BadRequest(new { message = errorMessage });
        }

        var profile = StudentProfile.CreateSelfRegistration(firstName, lastName, email);
        profile.UserId = user.Id;

        _dbContext.StudentProfiles.Add(profile);
        await _dbContext.SaveChangesAsync();

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
