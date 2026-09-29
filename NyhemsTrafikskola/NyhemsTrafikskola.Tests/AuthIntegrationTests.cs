using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NyhemsTrafikskola.API;
using NyhemsTrafikskola.Application.Authorization;
using NyhemsTrafikskola.Infrastructure;
using NyhemsTrafikskola.Infrastructure.Auth;

namespace NyhemsTrafikskola.Tests;

public class AuthIntegrationTests
{
    [Test]
    public async Task Login_WhenCredentialsAreValid_ReturnsOkAndMeReturnsUserInfo()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        await factory.SeedUserAsync("student@example.com", "Password123!", ApplicationRoles.Student);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password = "Password123!",
        });

        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(loginResponse.Headers.Contains("X-XSRF-TOKEN"), Is.True);

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToArray();
        var authCookie = cookies.Single(x => x.StartsWith(".NyhemsTrafikskola.Auth=", StringComparison.Ordinal));
        var csrfCookie = cookies.Single(x => x.StartsWith("NyhemsTrafikskola.XSRF=", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(authCookie, Does.Contain("secure"));
            Assert.That(authCookie, Does.Contain("httponly"));
            Assert.That(csrfCookie, Does.Contain("secure"));
            Assert.That(csrfCookie, Does.Not.Contain("httponly"));
        });

        var meResponse = await client.GetAsync("/api/auth/me");

        Assert.Multiple(() =>
        {
            Assert.That(meResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(meResponse.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        });

        var payload = await meResponse.Content.ReadFromJsonAsync<AuthMeResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Email, Is.EqualTo("student@example.com"));
            Assert.That(payload.Roles, Contains.Item(ApplicationRoles.Student));
        });
    }

    [Test]
    public async Task Login_WhenPasswordIsWrong_ReturnsUnauthorized()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        await factory.SeedUserAsync("student@example.com", "Password123!", ApplicationRoles.Student);

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password = "WrongPassword!",
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Me_WhenUserIsNotAuthenticated_ReturnsUnauthorized()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Logout_WhenRequestIncludesCsrfToken_EndsSession()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        await factory.SeedUserAsync("student@example.com", "Password123!", ApplicationRoles.Student);

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password = "Password123!",
        });

        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var me = await client.GetAsync("/api/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var csrfToken = factory.GetAntiforgeryTokenFromResponse(me);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrfToken);

        var logout = await client.PostAsync("/api/auth/logout", null);

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Logout_WhenRequestDoesNotIncludeCsrfToken_ReturnsBadRequest()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        await factory.SeedUserAsync("student@example.com", "Password123!", ApplicationRoles.Student);

        await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password = "Password123!",
        });

        var me = await client.GetAsync("/api/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logout = await client.PostAsync("/api/auth/logout", null);

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Logout_WhenRequestIncludesInvalidCsrfToken_ReturnsBadRequest()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedUserAsync("student@example.com", "Password123!", ApplicationRoles.Student);

        await client.PostAsJsonAsync("/api/auth/login", new { email = "student@example.com", password = "Password123!" });
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "invalid-token");

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AdminEndpoint_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/test-authorization/admin");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AdminEndpoint_WhenUserDoesNotHaveRequiredRole_ReturnsForbidden()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedUserAsync("student@example.com", "Password123!", ApplicationRoles.Student);
        await client.PostAsJsonAsync("/api/auth/login", new { email = "student@example.com", password = "Password123!" });

        var response = await client.GetAsync("/api/test-authorization/admin");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RoleInitializer_WhenRunMultipleTimes_DoesNotDuplicateRoles()
    {
        await using var factory = new ApiWebApplicationFactory();

        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var initializer = new RoleInitializer(roleManager);

        await initializer.EnsureRolesAsync();
        await initializer.EnsureRolesAsync();

        foreach (var role in new[]
        {
            ApplicationRoles.Student,
            ApplicationRoles.Instructor,
            ApplicationRoles.Admin,
        })
        {
            Assert.That(await roleManager.RoleExistsAsync(role), Is.True);
        }
    }

    [Test]
    public void StudentCannotAssignThemselvesPersonalRole()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Student]), Is.True);
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Instructor]), Is.False);
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Admin]), Is.False);
        });
    }

    private sealed record AuthMeResponse
    {
        public string Id { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string[] Roles { get; init; } = Array.Empty<string>();
        public Guid? StudentProfileId { get; init; }
        public Guid? InstructorProfileId { get; init; }
    }

    private sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public new HttpClient CreateClient(WebApplicationFactoryClientOptions? options = null)
        {
            options ??= new WebApplicationFactoryClientOptions();
            options.BaseAddress = new Uri("https://localhost");
            return base.CreateClient(options);
        }

        public ApiWebApplicationFactory()
        {
            _connection.Open();

            using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options);

            context.Database.EnsureCreated();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlite(_connection));
                services.AddControllers().AddApplicationPart(typeof(TestAuthorizationController).Assembly);
            });
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }

        public async Task SeedUserAsync(string email, string password, string role)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            await dbContext.Database.EnsureCreatedAsync();

            foreach (var existingRole in new[]
            {
                ApplicationRoles.Student,
                ApplicationRoles.Instructor,
                ApplicationRoles.Admin,
            })
            {
                if (!await roleManager.RoleExistsAsync(existingRole))
                {
                    await roleManager.CreateAsync(new IdentityRole(existingRole));
                }
            }

            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                if (!await userManager.IsInRoleAsync(existingUser, role))
                {
                    await userManager.AddToRoleAsync(existingUser, role);
                }

                return;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = "Test",
                LastName = "User",
                EmailConfirmed = true,
                IsActive = true,
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            }

            await userManager.AddToRoleAsync(user, role);
        }

        public string GetAntiforgeryTokenFromResponse(params HttpResponseMessage[] responses)
        {
            foreach (var response in responses)
            {
                if (response.Headers.TryGetValues("X-XSRF-TOKEN", out var tokens))
                {
                    return tokens.Single();
                }
            }

            throw new InvalidOperationException("Antiforgery request token was not returned.");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _connection.Dispose();
        }
    }
}

[ApiController]
[Route("api/test-authorization/admin")]
[Authorize(Policy = "RequireAdmin")]
public sealed class TestAuthorizationController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
