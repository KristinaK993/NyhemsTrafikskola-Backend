using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using NyhemsTrafikskola.API.Controllers;
using NyhemsTrafikskola.Application.Administration;
using NyhemsTrafikskola.Application.Authorization;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Domain;
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

        var password = NewTestPassword();
        await factory.SeedUserAsync("student@example.com", password, ApplicationRoles.Student);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password,
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

        await factory.SeedUserAsync("student@example.com", NewTestPassword(), ApplicationRoles.Student);
        var incorrectPassword = NewTestPassword();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password = incorrectPassword,
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

        var password = NewTestPassword();
        await factory.SeedUserAsync("student@example.com", password, ApplicationRoles.Student);

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password,
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

        var password = NewTestPassword();
        await factory.SeedUserAsync("student@example.com", password, ApplicationRoles.Student);

        await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "student@example.com",
            password,
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
        var password = NewTestPassword();
        await factory.SeedUserAsync("student@example.com", password, ApplicationRoles.Student);

        await client.PostAsJsonAsync("/api/auth/login", new { email = "student@example.com", password });
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
        var password = NewTestPassword();
        await factory.SeedUserAsync("student@example.com", password, ApplicationRoles.Student);
        await client.PostAsJsonAsync("/api/auth/login", new { email = "student@example.com", password });

        var response = await client.GetAsync("/api/test-authorization/admin");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase(ApplicationRoles.Admin)]
    [TestCase(ApplicationRoles.Instructor)]
    public async Task AdminCourseSessions_WhenUserHasStaffRole_AllowsListing(string role)
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanBil);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: false);
        await SignInAsync(factory, client, $"{role.ToLowerInvariant()}@example.com", role);

        var response = await client.GetAsync("/api/admin/course-sessions");
        var sessions = await response.Content.ReadFromJsonAsync<AdminCourseSessionResponse[]>();
        var start = DateTimeOffset.UtcNow.AddDays(7);
        var create = await client.PostAsJsonAsync("/api/admin/course-sessions", new
        {
            courseId,
            startTime = start,
            endTime = start.AddHours(2),
            capacity = 16,
        });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sessions, Has.Some.Matches<AdminCourseSessionResponse>(x => x.Id == session.Id && !x.IsPublished));
            Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        });
    }

    [TestCase(ApplicationRoles.Admin)]
    [TestCase(ApplicationRoles.Instructor)]
    public async Task AdminCourseSessions_WhenStaffBooksManually_CreatesConfirmedBookingWithoutCreatingAccount(string role)
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanBil);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: true);
        await SignInAsync(factory, client, $"{role.ToLowerInvariant()}@example.com", role);

        var response = await client.PostAsJsonAsync($"/api/admin/course-sessions/{session.Id}/bookings", new
        {
            firstName = "Anna",
            lastName = "Andersson",
            phone = "0701234567",
            email = "anna@example.com",
        });
        var details = await response.Content.ReadFromJsonAsync<AdminCourseSessionDetailsResponse>();
        var summaryResponse = await client.GetAsync("/api/admin/course-sessions");
        using var summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        var sessionSummary = summaryJson.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == session.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(details!.ActiveBookingCount, Is.EqualTo(1));
            Assert.That(details.AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity - 1));
            Assert.That(details.Bookings.Single().Status, Is.EqualTo(nameof(BookingStatus.Confirmed)));
            Assert.That(details.Bookings.Single().Email, Is.EqualTo("anna@example.com"));
            Assert.That(details.Bookings.Single().FirstName, Is.EqualTo("Anna"));
            Assert.That(summaryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sessionSummary.GetProperty("activeBookingCount").GetInt32(), Is.EqualTo(1));
            Assert.That(sessionSummary.GetProperty("availableSeats").GetInt32(), Is.EqualTo(CourseSession.StandardCapacity - 1));
            Assert.That(sessionSummary.GetProperty("bookingHistoryCount").GetInt32(), Is.EqualTo(1));
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(await db.Users.CountAsync(), Is.EqualTo(1), "Manual enrollment must not create a password-based account.");
        Assert.That(await db.StudentProfiles.CountAsync(), Is.EqualTo(1));
        Assert.That((await db.StudentProfiles.SingleAsync()).UserId, Is.Null);
    }

    [Test]
    public async Task AdminCourseSessions_WhenStaffBooksExistingStudent_ReusesAccountAndCreatesMissingProfile()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanMC);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: true);
        var studentPassword = NewTestPassword();
        await factory.SeedUserAsync("existing@example.com", studentPassword, ApplicationRoles.Student);
        string studentUserId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var studentUser = await db.Users.SingleAsync(x => x.Email == "existing@example.com");
            studentUserId = studentUser.Id;
        }
        await SignInAsync(factory, client, "admin@example.com", ApplicationRoles.Admin);

        var response = await client.PostAsJsonAsync($"/api/admin/course-sessions/{session.Id}/bookings", new
        {
            firstName = "Erik",
            lastName = "Ek",
            phone = "0701234567",
            email = "EXISTING@example.com",
        });

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(verifyDb.StudentProfiles.Count(), Is.EqualTo(1));
            Assert.That(verifyDb.Users.Count(), Is.EqualTo(2));
            Assert.That(verifyDb.StudentProfiles.Single().UserId, Is.EqualTo(studentUserId));
            Assert.That(verifyDb.CourseBookings.Single().StudentProfileId, Is.EqualTo(verifyDb.StudentProfiles.Single().Id));
        });
    }

    [Test]
    public async Task AdminCourseSessions_WhenStudentRequestsList_ReturnsForbidden()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(factory, client, "student@example.com", ApplicationRoles.Student);

        var response = await client.GetAsync("/api/admin/course-sessions");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AdminCourseSessions_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/admin/course-sessions");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(ApplicationRoles.Admin)]
    [TestCase(ApplicationRoles.Instructor)]
    public async Task AdminCourses_WhenUserHasStaffRole_ReturnsDatabaseCoursesWithIds(string role)
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var carCourseId = await factory.SeedCourseAsync(CourseType.RiskettanBil);
        var duplicateCarCourseId = await factory.SeedCourseAsync(CourseType.RiskettanBil);
        var mcCourseId = await factory.SeedCourseAsync(CourseType.RiskettanMC);
        var inactiveCourseId = await factory.SeedCourseAsync(CourseType.RiskettanMC, isActive: false);
        await SignInAsync(factory, client, $"{role.ToLowerInvariant()}@example.com", role);

        var response = await client.GetAsync("/api/admin/courses");
        var courses = await response.Content.ReadFromJsonAsync<AdminCourseResponse[]>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(courses, Has.Some.Matches<AdminCourseResponse>(course => course.Id == carCourseId && course.Type == nameof(CourseType.RiskettanBil)));
            Assert.That(courses, Has.Some.Matches<AdminCourseResponse>(course => course.Id == duplicateCarCourseId && course.Type == nameof(CourseType.RiskettanBil)));
            Assert.That(courses, Has.Some.Matches<AdminCourseResponse>(course => course.Id == mcCourseId && course.Type == nameof(CourseType.RiskettanMC)));
            Assert.That(courses, Has.Some.Matches<AdminCourseResponse>(course => course.Id == inactiveCourseId && !course.IsActive));
            Assert.That(courses, Has.Length.EqualTo(4));
        });
    }

    [Test]
    public async Task AdminCourses_WhenStudentRequestsList_ReturnsForbidden()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var password = NewTestPassword();
        await factory.SeedUserAsync("student@example.com", password, ApplicationRoles.Student);
        await client.PostAsJsonAsync("/api/auth/login", new { email = "student@example.com", password });

        var response = await client.GetAsync("/api/admin/courses");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AdminCourseSessionDetails_WhenStaffCancelsBookingAndSession_PreservesHistoryAndHidesSessionPublicly()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanBil);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: true, capacity: 3);
        var booking = CourseBooking.CreatePendingReservation(session.Id, "Alice Andersson", "alice@example.com", "0701234567");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CourseBookings.Add(booking);
            await db.SaveChangesAsync();
        }
        await SignInAsync(factory, client, "admin@example.com", ApplicationRoles.Admin);

        var getDetails = await client.GetAsync($"/api/admin/course-sessions/{session.Id}");
        var details = await getDetails.Content.ReadFromJsonAsync<AdminCourseSessionDetailsResponse>();
        var cancelBooking = await client.PostAsync($"/api/admin/course-sessions/{session.Id}/bookings/{booking.Id}/cancel", null);
        var afterBookingCancel = await cancelBooking.Content.ReadFromJsonAsync<AdminCourseSessionDetailsResponse>();
        var cancelSession = await client.PostAsync($"/api/admin/course-sessions/{session.Id}/cancel", null);
        var afterSessionCancel = await cancelSession.Content.ReadFromJsonAsync<AdminCourseSessionDetailsResponse>();
        var publicSessions = await client.GetFromJsonAsync<CourseSessionResponseDto[]>("/api/course-sessions?courseType=RiskettanBil");

        Assert.Multiple(() =>
        {
            Assert.That(getDetails.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(details!.Bookings.Single().Email, Is.EqualTo("alice@example.com"));
            Assert.That(details.Bookings.Single().Phone, Is.EqualTo("0701234567"));
            Assert.That(details.ActiveBookingCount, Is.EqualTo(1));
            Assert.That(cancelBooking.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterBookingCancel!.Bookings.Single().Status, Is.EqualTo(nameof(BookingStatus.Cancelled)));
            Assert.That(afterBookingCancel.AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity));
            Assert.That(cancelSession.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterSessionCancel!.IsCancelled, Is.True);
            Assert.That(afterSessionCancel.AvailableSeats, Is.Zero);
            Assert.That(publicSessions!.Any(x => x.CourseSessionId == session.Id), Is.False);
        });

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(await verifyDb.CourseBookings.AnyAsync(x => x.Id == booking.Id && x.Status == BookingStatus.Cancelled), Is.True);
    }

    [Test]
    public async Task AdminCourseSessionDetails_WhenStudentRequestsOrCancels_ReturnsForbidden()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanMC);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: true);
        await SignInAsync(factory, client, "student@example.com", ApplicationRoles.Student);

        var details = await client.GetAsync($"/api/admin/course-sessions/{session.Id}");
        var cancelBooking = await client.PostAsync($"/api/admin/course-sessions/{session.Id}/bookings/{Guid.NewGuid()}/cancel", null);
        var cancelSession = await client.PostAsync($"/api/admin/course-sessions/{session.Id}/cancel", null);
        var archiveSession = await client.PostAsync($"/api/admin/course-sessions/{session.Id}/archive", null);
        var manualBooking = await client.PostAsJsonAsync($"/api/admin/course-sessions/{session.Id}/bookings", new
        {
            firstName = "Student",
            lastName = "Person",
            phone = "0701234567",
            email = "newstudent@example.com",
        });

        Assert.Multiple(() =>
        {
            Assert.That(details.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(cancelBooking.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(cancelSession.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(archiveSession.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(manualBooking.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public async Task AdminCourseSessions_WhenWritingWithoutCsrfToken_RejectsAllWriteMethods()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInWithoutCsrfHeaderAsync(factory, client, "admin@example.com", ApplicationRoles.Admin);
        var sessionId = Guid.NewGuid();

        var responses = new[]
        {
            await client.PostAsJsonAsync("/api/admin/course-sessions", new
            {
                courseId = Guid.NewGuid(),
                startTime = DateTimeOffset.UtcNow.AddDays(3),
                endTime = DateTimeOffset.UtcNow.AddDays(3).AddHours(2),
                capacity = 10,
            }),
            await client.PutAsJsonAsync($"/api/admin/course-sessions/{sessionId}", new { capacity = 10 }),
            await client.PostAsync($"/api/admin/course-sessions/{sessionId}/publish", null),
            await client.PostAsJsonAsync($"/api/admin/course-sessions/{sessionId}/bookings", new
            {
                firstName = "No",
                lastName = "Token",
                phone = "0701234567",
                email = "no-token@example.com",
            }),
            await client.PostAsync($"/api/admin/course-sessions/{sessionId}/unpublish", null),
            await client.PostAsync($"/api/admin/course-sessions/{sessionId}/archive", null),
            await client.DeleteAsync($"/api/admin/course-sessions/{sessionId}"),
        };

        Assert.That(responses.Select(x => x.StatusCode), Is.All.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AdminCourseSessions_WhenStaffArchivesSession_PreservesHistoryAndHidesItFromPublicAndNormalLists()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanBil);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: true, capacity: 4);
        var booking = new CourseBooking(session.Id, "Alice Andersson", "alice@example.com", "0701234567");
        booking.Cancel();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CourseBookings.Add(booking);
            await db.SaveChangesAsync();
        }
        await SignInAsync(factory, client, "instructor@example.com", ApplicationRoles.Instructor);

        var archive = await client.PostAsync($"/api/admin/course-sessions/{session.Id}/archive", null);
        var normal = await client.GetFromJsonAsync<AdminCourseSessionResponse[]>("/api/admin/course-sessions");
        var archived = await client.GetFromJsonAsync<AdminCourseSessionResponse[]>("/api/admin/course-sessions?archived=true");
        var publicSessions = await client.GetFromJsonAsync<CourseSessionResponseDto[]>("/api/course-sessions?courseType=RiskettanBil");

        Assert.Multiple(() =>
        {
            Assert.That(archive.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(normal!.Any(x => x.Id == session.Id), Is.False);
            Assert.That(archived!.Any(x => x.Id == session.Id && x.IsArchived && !x.IsPublished && x.BookingHistoryCount == 1), Is.True);
            Assert.That(publicSessions!.Any(x => x.CourseSessionId == session.Id), Is.False);
        });
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(await verifyDb.CourseBookings.AnyAsync(x => x.Id == booking.Id && x.Status == BookingStatus.Cancelled), Is.True);
    }

    [TestCase(CourseType.RiskettanBil)]
    [TestCase(CourseType.RiskettanMC)]
    public async Task AdminCourseSessions_WhenRequestIsValid_CreatesUpdatesPublishesAndDeletes(CourseType courseType)
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(courseType);
        await SignInAsync(factory, client, "admin@example.com", ApplicationRoles.Admin);

        var start = DateTimeOffset.UtcNow.AddDays(10);
        var end = start.AddHours(2);
        var create = await client.PostAsJsonAsync("/api/admin/course-sessions", new
        {
            courseId,
            startTime = start,
            endTime = end,
            capacity = 16,
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await create.Content.ReadFromJsonAsync<AdminCourseSessionResponse>();
        Assert.That(created, Is.Not.Null);

        var list = await client.GetFromJsonAsync<AdminCourseSessionResponse[]>("/api/admin/course-sessions");
        Assert.That(list, Has.Some.Matches<AdminCourseSessionResponse>(x => x.Id == created!.Id && !x.IsPublished));

        var updatedStart = start.AddDays(1);
        var updatedEnd = end.AddDays(1);
        var update = await client.PutAsJsonAsync($"/api/admin/course-sessions/{created!.Id}", new
        {
            startTime = updatedStart,
            endTime = updatedEnd,
            capacity = 16,
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = await update.Content.ReadFromJsonAsync<AdminCourseSessionResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(updated!.StartTime, Is.EqualTo(updatedStart));
            Assert.That(updated.EndTime, Is.EqualTo(updatedEnd));
            Assert.That(updated.Capacity, Is.EqualTo(16));
        });

        Assert.That((await client.PostAsync($"/api/admin/course-sessions/{created.Id}/publish", null)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var published = await client.GetFromJsonAsync<AdminCourseSessionResponse[]>("/api/admin/course-sessions");
        Assert.That(published, Has.Some.Matches<AdminCourseSessionResponse>(x => x.Id == created.Id && x.IsPublished));

        Assert.That((await client.PostAsync($"/api/admin/course-sessions/{created.Id}/unpublish", null)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await client.DeleteAsync($"/api/admin/course-sessions/{created.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task AdminCourseSessions_WhenSessionHasBookingHistory_PreservesCapacityAndDeleteRules()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanBil);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: true, capacity: 5);
        await factory.AddConfirmedBookingsAsync(session.Id, count: 2);
        await SignInAsync(factory, client, "admin@example.com", ApplicationRoles.Admin);

        var update = await client.PutAsJsonAsync($"/api/admin/course-sessions/{session.Id}", new { capacity = 1 });
        var delete = await client.DeleteAsync($"/api/admin/course-sessions/{session.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        });
    }

    [Test]
    public async Task AdminCourseSessions_RejectsNonstandardRiskettaCapacity()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var courseId = await factory.SeedCourseAsync(CourseType.RiskettanMC);
        var session = await factory.SeedCourseSessionAsync(courseId, isPublished: false);
        await SignInAsync(factory, client, "admin@example.com", ApplicationRoles.Admin);

        var start = DateTimeOffset.UtcNow.AddDays(8);
        var create = await client.PostAsJsonAsync("/api/admin/course-sessions", new
        {
            courseId,
            startTime = start,
            endTime = start.AddHours(2),
            capacity = 20,
        });
        var update = await client.PutAsJsonAsync($"/api/admin/course-sessions/{session.Id}", new { capacity = 20 });

        Assert.Multiple(() =>
        {
            Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task AdminCourseSessions_WhenCourseIsInactive_RejectsCreation()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var inactiveCourseId = await factory.SeedCourseAsync(CourseType.RiskettanMC, isActive: false);
        await SignInAsync(factory, client, "admin@example.com", ApplicationRoles.Admin);
        var start = DateTimeOffset.UtcNow.AddDays(5);

        var response = await client.PostAsJsonAsync("/api/admin/course-sessions", new
        {
            courseId = inactiveCourseId,
            startTime = start,
            endTime = start.AddHours(2),
            capacity = 16,
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
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
    public async Task ApiStartup_DoesNotCreateAnAdministrator()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.That(await userManager.GetUsersInRoleAsync(ApplicationRoles.Admin), Is.Empty);
    }

    [Test]
    public async Task FirstAdminProvisioner_WhenAdminRoleIsMissing_CreatesIdentityUserAndAdminRole()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();
        var password = NewTestPassword();
        var adminRole = await roleManager.FindByNameAsync(ApplicationRoles.Admin);
        Assert.That(adminRole, Is.Not.Null);
        Assert.That((await roleManager.DeleteAsync(adminRole!)).Succeeded, Is.True);

        var result = await provisioner.CreateFirstAdminAsync(
            "  principal@example.com  ",
            password,
            "Principal",
            "Administrator");
        var user = await userManager.FindByEmailAsync("principal@example.com");
        var adminRoleExists = await roleManager.RoleExistsAsync(ApplicationRoles.Admin);

        Assert.Multiple(() =>
        {
            Assert.That(result.WasCreated, Is.True);
            Assert.That(result.AdminAlreadyExists, Is.False);
            Assert.That(adminRoleExists, Is.True);
            Assert.That(user, Is.Not.Null);
            Assert.That(user!.PasswordHash, Is.Not.Empty);
        });
        Assert.That(await userManager.IsInRoleAsync(user!, ApplicationRoles.Admin), Is.True);
        Assert.That(await userManager.CheckPasswordAsync(user!, password), Is.True);
    }

    [Test]
    public async Task FirstAdminProvisioner_WhenAdminAlreadyExists_DoesNotCreateAnotherAccount()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();

        var first = await provisioner.CreateFirstAdminAsync("first@example.com", NewTestPassword(), "First", "Admin");
        var second = await provisioner.CreateFirstAdminAsync("second@example.com", NewTestPassword(), "Second", "Admin");

        Assert.Multiple(() =>
        {
            Assert.That(first.WasCreated, Is.True);
            Assert.That(second.WasCreated, Is.False);
            Assert.That(second.AdminAlreadyExists, Is.True);
        });
        Assert.That(await userManager.FindByEmailAsync("second@example.com"), Is.Null);
        Assert.That(await userManager.GetUsersInRoleAsync(ApplicationRoles.Admin), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FirstAdminProvisioner_WhenEmailBelongsToExistingStudent_DoesNotPromoteStudent()
    {
        await using var factory = new ApiWebApplicationFactory();
        await factory.SeedUserAsync("student@example.com", NewTestPassword(), ApplicationRoles.Student);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();

        var result = await provisioner.CreateFirstAdminAsync("student@example.com", NewTestPassword(), "Student", "Account");
        var student = await userManager.FindByEmailAsync("student@example.com");

        Assert.That(result.WasCreated, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(await userManager.IsInRoleAsync(student!, ApplicationRoles.Admin), Is.False);
        Assert.That(await userManager.GetUsersInRoleAsync(ApplicationRoles.Admin), Is.Empty);
    }

    [Test]
    public async Task FirstAdminProvisioner_WhenPasswordFailsIdentityPolicy_DoesNotCreateUser()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();

        var result = await provisioner.CreateFirstAdminAsync("weak@example.com", NewWeakTestPassword(), "Weak", "Test");

        Assert.That(result.WasCreated, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(await userManager.FindByEmailAsync("weak@example.com"), Is.Null);
    }

    [Test]
    public void StudentCannotAssignThemselvesPersonalRole()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Student]), Is.True);
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Student, ApplicationRoles.Admin]), Is.False);
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Student, ApplicationRoles.Instructor]), Is.False);
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

    private sealed record AdminCourseSessionResponse(
        Guid Id,
        Guid CourseId,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        int Capacity,
        bool IsPublished,
        bool IsCancelled,
        bool IsArchived,
        int ActiveBookingCount,
        int AvailableSeats,
        int BookingHistoryCount);

    private sealed record AdminCourseResponse(Guid Id, string Type, string Title, decimal Price, bool IsActive);

    private sealed record AdminCourseSessionDetailsResponse(
        Guid CourseSessionId,
        int Capacity,
        int ActiveBookingCount,
        int AvailableSeats,
        bool IsCancelled,
        AdminBookingResponse[] Bookings);

    private sealed record AdminBookingResponse(Guid Id, string CustomerName, string? FirstName, string? LastName, string Email, string Phone, DateTimeOffset CreatedAt, string Status, bool CountsTowardCapacity);

    private static async Task SignInAsync(ApiWebApplicationFactory factory, HttpClient client, string email, string role)
    {
        await SignInWithoutCsrfHeaderAsync(factory, client, email, role);
        var me = await client.GetAsync("/api/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var currentUser = await me.Content.ReadFromJsonAsync<AuthMeResponse>();
        Assert.That(currentUser?.Roles, Contains.Item(role));
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", factory.GetAntiforgeryTokenFromResponse(me));
    }

    private static async Task SignInWithoutCsrfHeaderAsync(ApiWebApplicationFactory factory, HttpClient client, string email, string role)
    {
        var password = NewTestPassword();
        await factory.SeedUserAsync(email, password, role);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static string NewTestPassword() => $"T{Guid.NewGuid():N}a1";

    private static string NewWeakTestPassword() => Guid.NewGuid().ToString("N")[..4];

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

        public async Task<Guid> SeedCourseAsync(CourseType courseType, bool isActive = true)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var course = new Course(courseType, courseType.ToString(), 3200m, isActive);
            dbContext.Courses.Add(course);
            await dbContext.SaveChangesAsync();
            return course.Id;
        }

        public async Task<CourseSession> SeedCourseSessionAsync(Guid courseId, bool isPublished, int capacity = 10)
        {
            using var scope = Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICourseSessionAdministrationService>();
            var start = DateTimeOffset.UtcNow.AddDays(5);
            return await service.CreateCourseSessionAsync(courseId, start, start.AddHours(2), capacity, isPublished);
        }

        public async Task AddConfirmedBookingsAsync(Guid courseSessionId, int count)
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < count; index++)
            {
                var student = new StudentProfile($"Student{index}", "Test", $"student{index}@example.com");
                dbContext.StudentProfiles.Add(student);
                await dbContext.SaveChangesAsync();

                var booking = new CourseBooking(courseSessionId, student.FirstName + " Test", student.Email, "0701234567", student.Id);
                booking.Confirm();
                dbContext.CourseBookings.Add(booking);
            }

            await dbContext.SaveChangesAsync();
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
