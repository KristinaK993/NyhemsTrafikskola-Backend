using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
            Roles = roles,
            StudentProfileId = studentProfileId,
            InstructorProfileId = instructorProfileId,
        });
    }

    public sealed record LoginRequest(string Email, string Password);

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
        public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
        public Guid? StudentProfileId { get; init; }
        public Guid? InstructorProfileId { get; init; }
    }
}
