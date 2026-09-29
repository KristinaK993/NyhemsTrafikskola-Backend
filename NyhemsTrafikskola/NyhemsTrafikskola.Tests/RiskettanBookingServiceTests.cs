using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Domain;
using NyhemsTrafikskola.Infrastructure;

namespace NyhemsTrafikskola.Tests;

public class RiskettanBookingServiceTests
{
    [Test]
    public async Task GetAvailableSeats_ReturnsCapacityMinusBookedActiveSlots()
    {
        await using var context = CreateContext();
        var service = new CourseBookingService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        var session = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 5, true);
        var confirmedStudent = new StudentProfile("A", "Andersson", "a@example.com");
        var cancelledStudent = new StudentProfile("B", "Bengtsson", "b@example.com");
        var activePendingStudent = new StudentProfile("C", "Carlsson", "c@example.com");
        var expiredPendingStudent = new StudentProfile("D", "Dahl", "d@example.com");

        context.Courses.Add(course);
        context.CourseSessions.Add(session);
        context.StudentProfiles.AddRange(confirmedStudent, cancelledStudent, activePendingStudent, expiredPendingStudent);

        var confirmed = new CourseBooking(session.Id, "A", "a@example.com", "0701111111", confirmedStudent.Id);
        confirmed.Confirm();

        var cancelled = new CourseBooking(session.Id, "B", "b@example.com", "0702222222", cancelledStudent.Id);
        cancelled.Cancel();

        var activePending = CourseBooking.CreatePendingReservation(
            session.Id,
            "C",
            "c@example.com",
            "0703333333",
            activePendingStudent.Id);

        var expiredPending = CourseBooking.CreatePendingReservation(
            session.Id,
            "D",
            "d@example.com",
            "0704444444",
            expiredPendingStudent.Id,
            DateTimeOffset.UtcNow.AddMinutes(-10));

        context.CourseBookings.AddRange(confirmed, cancelled, activePending, expiredPending);
        await context.SaveChangesAsync();

        var available = await service.GetAvailableSeatsAsync(session.Id);

        Assert.That(available, Is.EqualTo(CourseSession.StandardCapacity - 2));
    }

    [Test]
    public async Task CreatePendingReservation_StoresReservationWithExpiry()
    {
        await using var context = CreateContext();
        var service = new CourseBookingService(context);

        var course = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m, true);
        var session = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(3).AddHours(2), 4, true);
        context.Courses.Add(course);
        context.CourseSessions.Add(session);
        await context.SaveChangesAsync();

        var result = await service.CreatePendingReservationAsync(
            session.Id,
            "Eva Eriksson",
            "eva@example.com",
            "0705555555");

        var booking = await context.CourseBookings.SingleAsync(x => x.Id == result.BookingId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(BookingStatus.Pending));
            Assert.That(booking.ExpiresAt, Is.Not.Null);
            Assert.That(booking.ExpiresAt!.Value, Is.GreaterThan(DateTimeOffset.UtcNow));
            Assert.That(booking.ExpiresAt!.Value - booking.CreatedAt, Is.GreaterThan(TimeSpan.FromMinutes(29)));
        });
    }

    [Test]
    public async Task ConfirmPendingReservation_WhenExpired_Throws()
    {
        await using var context = CreateContext();
        var service = new CourseBookingService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        var session = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(2), 4, true);
        var student = new StudentProfile("Erik", "Eriksson", "erik@example.com");

        context.Courses.Add(course);
        context.CourseSessions.Add(session);
        context.StudentProfiles.Add(student);
        await context.SaveChangesAsync();

        var booking = CourseBooking.CreatePendingReservation(
            session.Id,
            "Erik Eriksson",
            "erik@example.com",
            "0707777777",
            student.Id,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        context.CourseBookings.Add(booking);
        await context.SaveChangesAsync();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ConfirmPendingReservationAsync(booking.Id, student.Id));

        Assert.That(ex!.Message, Does.Contain("expired"));
    }

    [Test]
    public async Task CreateDirectConfirmedBooking_ForAuthenticatedStudent_StoresConfirmedBooking()
    {
        await using var context = CreateContext();
        var service = new CourseBookingService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        var session = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 3, true);
        var student = new StudentProfile("Sara", "Svensson", "sara@example.com");

        context.Courses.Add(course);
        context.CourseSessions.Add(session);
        context.StudentProfiles.Add(student);
        await context.SaveChangesAsync();

        var result = await service.CreateDirectConfirmedBookingAsync(
            session.Id,
            student.Id,
            "Sara Svensson",
            "sara@example.com",
            "0701234567");

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(BookingStatus.Confirmed));
            Assert.That(result.AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity - 1));
        });
    }

    [Test]
    public async Task CreateBooking_WhenSessionIsFull_Throws()
    {
        await using var context = CreateContext();
        var service = new CourseBookingService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        var session = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(2), 2, true);
        context.Courses.Add(course);
        context.CourseSessions.Add(session);
        context.CourseBookings.AddRange(Enumerable.Range(0, CourseSession.StandardCapacity)
            .Select(index => CourseBooking.CreatePendingReservation(session.Id, $"Student {index}", $"student{index}@example.com", "0701111111")));
        await context.SaveChangesAsync();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreatePendingReservationAsync(
                session.Id,
                "C",
                "c@example.com",
                "0703333333"));

        Assert.That(ex!.Message, Does.Contain("full"));
    }

    [Test]
    public async Task CreateBooking_WhenSessionIsUnpublishedOrPassed_Throws()
    {
        await using var context = CreateContext();
        var service = new CourseBookingService(context);

        var course = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m, true);
        var publishedFuture = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(2), 4, true);
        var unpublishedFuture = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 4, false);
        var passed = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-2).AddHours(2), 4, true);

        context.Courses.Add(course);
        context.CourseSessions.AddRange(publishedFuture, unpublishedFuture, passed);
        await context.SaveChangesAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreatePendingReservationAsync(unpublishedFuture.Id, "Z", "z@example.com", "0709999999"));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreatePendingReservationAsync(passed.Id, "Y", "y@example.com", "0708888888"));
    }

    [Test]
    public async Task CreateBooking_WhenSameStudentTriesToBookSameSessionTwice_Throws()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Database.EnsureCreated();

            var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
            var session = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 2, true);
            var student = new StudentProfile("Alice", "Andersson", "alice@example.com");

            seed.Courses.Add(course);
            seed.CourseSessions.Add(session);
            seed.StudentProfiles.Add(student);
            await seed.SaveChangesAsync();
        }

        await using var serviceContext = new ApplicationDbContext(options);
        var service = new CourseBookingService(serviceContext);
        var sessionId = await serviceContext.CourseSessions.SingleAsync();
        var studentId = await serviceContext.StudentProfiles.SingleAsync();

        var firstResult = await service.CreateDirectConfirmedBookingAsync(
            sessionId.Id,
            studentId.Id,
            "Alice Andersson",
            "alice@example.com",
            "0701111111");

        Assert.That(firstResult.Status, Is.EqualTo(BookingStatus.Confirmed));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreateDirectConfirmedBookingAsync(
                sessionId.Id,
                studentId.Id,
                "Alice Andersson",
                "alice@example.com",
                "0702222222"));

        Assert.That(ex!.Message, Does.Contain("active booking"));
    }

    [Test]
    public async Task CreateBooking_WhenTwoRequestsHitLastSeat_OnlyOneSucceeds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Database.EnsureCreated();

            var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
            var session = new CourseSession(course.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 1, true);
            seed.Courses.Add(course);
            seed.CourseSessions.Add(session);
            seed.CourseBookings.AddRange(Enumerable.Range(0, CourseSession.StandardCapacity - 1)
                .Select(index => CourseBooking.CreatePendingReservation(session.Id, $"Existing {index}", $"existing{index}@example.com", "0701111111")));
            await seed.SaveChangesAsync();
        }

        async Task<CreateBookingResult?> ExecuteAsync(string name, string email, string phone)
        {
            try
            {
                await using var db = new ApplicationDbContext(options);
                var service = new CourseBookingService(db);
                var sessionId = await db.CourseSessions.SingleAsync();
                return await service.CreatePendingReservationAsync(sessionId.Id, name, email, phone);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        var results = await Task.WhenAll(
            ExecuteAsync("Alice", "alice@example.com", "0701111111"),
            ExecuteAsync("Bob", "bob@example.com", "0702222222"));

        Assert.That(results.Count(x => x is not null), Is.EqualTo(1));
    }

    private static ApplicationDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
