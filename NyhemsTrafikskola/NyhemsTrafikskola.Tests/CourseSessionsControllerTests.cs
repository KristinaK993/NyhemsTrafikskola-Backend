using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.API.Controllers;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Domain;
using NyhemsTrafikskola.Infrastructure;

namespace NyhemsTrafikskola.Tests;

public class CourseSessionsControllerTests
{
    [Test]
    public async Task Get_WhenCourseTypeIsValid_ReturnsPublishedFutureSessionsForRiskettanBil()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            context.Database.EnsureCreated();

            var bilCourse = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
            var mcCourse = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m, true);
            var inactiveCourse = new Course(CourseType.RiskettanBil, "Old inactive", 2500m, false);

            var futureBil = new CourseSession(bilCourse.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 5, true);
            var futureMc = new CourseSession(mcCourse.Id, DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(3).AddHours(2), 4, true);
            var pastBil = new CourseSession(bilCourse.Id, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-2).AddHours(2), 5, true);
            var unpublishedBil = new CourseSession(bilCourse.Id, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(2), 6, false);

            var studentOne = new StudentProfile("Alice", "Andersson", "a@example.com");
            var studentTwo = new StudentProfile("Bob", "Bengtsson", "b@example.com");

            context.Courses.AddRange(bilCourse, mcCourse, inactiveCourse);
            context.CourseSessions.AddRange(futureBil, futureMc, pastBil, unpublishedBil);
            context.StudentProfiles.AddRange(studentOne, studentTwo);

            var confirmed = new CourseBooking(futureBil.Id, "A", "a@example.com", "0701111111", studentOne.Id);
            confirmed.Confirm();
            var pending = CourseBooking.CreatePendingReservation(futureBil.Id, "B", "b@example.com", "0702222222", studentTwo.Id);

            context.CourseBookings.AddRange(confirmed, pending);
            await context.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);
        var service = new CourseBookingService(db);
        var controller = new CourseSessionsController(service);

        var result = await controller.Get("RiskettanBil", CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);

        var payload = ok!.Value as IEnumerable<CourseSessionResponseDto>;
        var items = payload!.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].CourseType, Is.EqualTo(CourseType.RiskettanBil));
            Assert.That(items[0].CourseTitle, Is.EqualTo("Riskettan Bil"));
            Assert.That(items[0].AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity - 2));
            Assert.That(items[0].TotalSeats, Is.EqualTo(CourseSession.StandardCapacity));
        });
    }

    [Test]
    public async Task Get_WhenCourseTypeIsInvalid_ReturnsBadRequest()
    {
        var service = new StubCourseBookingService();
        var controller = new CourseSessionsController(service);

        var result = await controller.Get("NotARealType", CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public void ControllerRoute_UsesLowercaseCourseSessionsRoute()
    {
        var route = typeof(CourseSessionsController).GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Single();

        Assert.That(route.Template, Is.EqualTo("api/course-sessions"));
    }

    [Test]
    public async Task Get_WhenNoSessionsExist_ReturnsOkWithEmptyList()
    {
        var service = new StubCourseBookingService(Array.Empty<CourseSessionAvailabilityDto>());
        var controller = new CourseSessionsController(service);

        var result = await controller.Get("RiskettanMC", CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.Value, Is.Empty);
    }

    private sealed class StubCourseBookingService : ICourseBookingService
    {
        private readonly IReadOnlyCollection<CourseSessionAvailabilityDto> _sessions;

        public StubCourseBookingService(IReadOnlyCollection<CourseSessionAvailabilityDto>? sessions = null)
        {
            _sessions = sessions ?? Array.Empty<CourseSessionAvailabilityDto>();
        }

        public Task<int> GetAvailableSeatsAsync(Guid courseSessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyCollection<CourseSessionAvailabilityDto>> GetPublishedFutureSessionsAsync(CourseType courseType, CancellationToken cancellationToken = default)
            => Task.FromResult(_sessions);

        public Task<CreateBookingResult> CreatePendingBookingAsync(CreateBookingRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CreateBookingResult> CreatePendingReservationAsync(Guid courseSessionId, string customerName, string email, string phone, Guid? studentProfileId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CreateBookingResult> CreateDirectConfirmedBookingAsync(Guid courseSessionId, Guid studentProfileId, string customerName, string email, string phone, BookingAudit? audit = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CreateBookingResult> ConfirmPendingReservationAsync(Guid bookingId, Guid studentProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CancelStudentBookingAsync(Guid bookingId, IReadOnlyCollection<Guid> ownStudentProfileIds, string? cancelledByUserId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
