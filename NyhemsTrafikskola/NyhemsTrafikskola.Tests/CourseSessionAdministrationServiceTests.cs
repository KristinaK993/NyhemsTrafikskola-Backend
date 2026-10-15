using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Administration;
using NyhemsTrafikskola.Application.Bookings;
using NyhemsTrafikskola.Domain;
using NyhemsTrafikskola.Infrastructure;

namespace NyhemsTrafikskola.Tests;

public class CourseSessionAdministrationServiceTests
{
    [Test]
    public async Task CreateCourseSessionAsync_WhenCourseExists_CreatesSession()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        var start = DateTimeOffset.UtcNow.AddDays(7);
        var end = start.AddHours(2);

        var session = await service.CreateCourseSessionAsync(course.Id, start, end, 10, true);

        Assert.Multiple(() =>
        {
            Assert.That(session.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(session.CourseId, Is.EqualTo(course.Id));
            Assert.That(session.Capacity, Is.EqualTo(CourseSession.StandardCapacity));
            Assert.That(session.IsPublished, Is.True);
            Assert.That(session.StartTime, Is.EqualTo(start));
            Assert.That(session.EndTime, Is.EqualTo(end));
        });
    }

    [Test]
    public async Task GetAllCourseSessionsAsync_IncludesUnpublishedSessions()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);

        var course = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m, true);
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        var first = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(2), 6, true);
        var second = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(2), 8, false);

        var all = await service.GetAllCourseSessionsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(all.Select(x => x.Id), Contains.Item(first.Id));
            Assert.That(all.Select(x => x.Id), Contains.Item(second.Id));
            Assert.That(all.Any(x => !x.IsPublished), Is.True);
        });
    }

    [Test]
    public async Task UpdateCourseSessionAsync_WhenCapacityWouldDropBelowActiveBookings_Throws()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        var student = new StudentProfile("Anna", "Andersson", "anna@example.com");
        context.Courses.Add(course);
        context.StudentProfiles.Add(student);
        await context.SaveChangesAsync();

        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), 3, true);

        var firstBooking = new CourseBooking(session.Id, "Anna Andersson", "anna@example.com", "0701111111", student.Id);
        firstBooking.Confirm();

        var secondStudent = new StudentProfile("Bengt", "Bengtsson", "bengt@example.com");
        context.StudentProfiles.Add(secondStudent);
        await context.SaveChangesAsync();

        var secondBooking = new CourseBooking(session.Id, "Bengt Bengtsson", "bengt@example.com", "0702222222", secondStudent.Id);
        secondBooking.Confirm();

        context.CourseBookings.AddRange(firstBooking, secondBooking);
        await context.SaveChangesAsync();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.UpdateCourseSessionAsync(session.Id, capacity: 1));

        Assert.That(ex!.Message, Does.Contain("capacity").IgnoreCase);
    }

    [Test]
    public async Task PublishAndUnpublishCourseSessionAsync_TogglesPublicationState()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);

        var course = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m, true);
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(8), DateTimeOffset.UtcNow.AddDays(8).AddHours(2), 5, false);

        await service.PublishCourseSessionAsync(session.Id);
        Assert.That((await context.CourseSessions.SingleAsync(x => x.Id == session.Id)).IsPublished, Is.True);
        Assert.That((await context.CourseSessions.SingleAsync(x => x.Id == session.Id)).Capacity, Is.EqualTo(CourseSession.StandardCapacity));

        await service.UnpublishCourseSessionAsync(session.Id);
        Assert.That((await context.CourseSessions.SingleAsync(x => x.Id == session.Id)).IsPublished, Is.False);
    }

    [Test]
    public async Task DeleteCourseSessionAsync_WhenBookingsExist_Throws()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        var student = new StudentProfile("Ola", "Ohlsson", "ola@example.com");
        context.Courses.Add(course);
        context.StudentProfiles.Add(student);
        await context.SaveChangesAsync();

        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(10), DateTimeOffset.UtcNow.AddDays(10).AddHours(2), 6, true);

        var booking = new CourseBooking(session.Id, "Ola Ohlsson", "ola@example.com", "0702222222", student.Id);
        booking.Confirm();
        context.CourseBookings.Add(booking);
        await context.SaveChangesAsync();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DeleteCourseSessionAsync(session.Id));

        Assert.That(ex!.Message, Does.Contain("booking"));
    }

    [Test]
    public async Task DeleteCourseSessionAsync_WhenNoBookingHistoryExists_RemovesSession()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);

        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, true);
        context.Courses.Add(course);
        await context.SaveChangesAsync();

        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(12), DateTimeOffset.UtcNow.AddDays(12).AddHours(2), 4, false);

        await service.DeleteCourseSessionAsync(session.Id);

        Assert.That(await context.CourseSessions.AnyAsync(x => x.Id == session.Id), Is.False);
    }

    [Test]
    public async Task ArchiveCourseSessionAsync_WithHistoryHidesAndUnpublishesWithoutDeletingBookings()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(2), 2, true);
        var booking = new CourseBooking(session.Id, "Anna Andersson", "anna@example.com", "0701111111");
        booking.Cancel();
        context.CourseBookings.Add(booking);
        await context.SaveChangesAsync();

        await service.ArchiveCourseSessionAsync(session.Id);

        var storedSession = await context.CourseSessions.SingleAsync(x => x.Id == session.Id);
        var normalSessions = await service.GetCourseSessionSummariesAsync(archived: false);
        var archivedSessions = await service.GetCourseSessionSummariesAsync(archived: true);
        Assert.Multiple(() =>
        {
            Assert.That(storedSession.IsArchived, Is.True);
            Assert.That(storedSession.IsPublished, Is.False);
            Assert.That(normalSessions, Is.Empty);
            Assert.That(archivedSessions.Single().BookingHistoryCount, Is.EqualTo(1));
        });
        Assert.That(await context.CourseBookings.AnyAsync(x => x.Id == booking.Id), Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await service.PublishCourseSessionAsync(session.Id));

        var bookingService = new CourseBookingService(context);
        Assert.That(await bookingService.GetAvailableSeatsAsync(session.Id), Is.Zero);
        var bookingException = Assert.ThrowsAsync<CourseBookingException>(async () => await bookingService.CreatePendingReservationAsync(session.Id, "Bengt Bengtsson", "bengt@example.com", "0702222222"));
        Assert.That(bookingException!.Error, Is.EqualTo(CourseBookingError.SessionArchived));
    }

    [Test]
    public async Task GetCourseSessionSummariesAsync_CountsOnlyBookingsThatOccupySeats()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        var student = new StudentProfile("Anna", "Andersson", "anna@example.com");
        context.StudentProfiles.Add(student);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(2), CourseSession.StandardCapacity);

        var confirmed = new CourseBooking(session.Id, "Anna Andersson", student.Email, "0701111111", student.Id);
        confirmed.Confirm();
        var activePending = CourseBooking.CreatePendingReservation(session.Id, "Bengt Berg", "bengt@example.com", "0702222222", null, DateTimeOffset.UtcNow.AddMinutes(10));
        var expiredPending = CourseBooking.CreatePendingReservation(session.Id, "Cecilia Carlsson", "cecilia@example.com", "0703333333", null, DateTimeOffset.UtcNow.AddMinutes(-10));
        var cancelled = new CourseBooking(session.Id, "David Dahl", "david@example.com", "0704444444");
        cancelled.Cancel();
        context.CourseBookings.AddRange(confirmed, activePending, expiredPending, cancelled);
        await context.SaveChangesAsync();

        var summary = (await service.GetCourseSessionSummariesAsync(archived: false)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(summary.BookingHistoryCount, Is.EqualTo(4));
            Assert.That(summary.ActiveBookingCount, Is.EqualTo(2));
            Assert.That(summary.AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity - 2));
        });
    }

    [TestCase(0, 0, 16)]
    [TestCase(1, 1, 15)]
    [TestCase(16, 16, 0)]
    public async Task GetCourseSessionSummariesAsync_ReturnsCorrectBookedAndAvailableCounts(int activeCount, int expectedBooked, int expectedAvailable)
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(2), CourseSession.StandardCapacity);
        var students = Enumerable.Range(0, activeCount)
            .Select(index => new StudentProfile($"Student{index}", "Test", $"student{index}@example.com"))
            .ToArray();
        context.StudentProfiles.AddRange(students);
        await context.SaveChangesAsync();

        var activeBookings = students.Select(student =>
        {
            var booking = new CourseBooking(session.Id, $"{student.FirstName} {student.LastName}", student.Email, "0701234567", student.Id);
            booking.Confirm();
            return booking;
        });
        var cancelled = new CourseBooking(session.Id, "Cancelled Student", "cancelled@example.com", "0701234567");
        cancelled.Cancel();
        var expiredPending = CourseBooking.CreatePendingReservation(session.Id, "Expired Student", "expired@example.com", "0701234567", null, DateTimeOffset.UtcNow.AddMinutes(-5));
        context.CourseBookings.AddRange(activeBookings.Append(cancelled).Append(expiredPending));
        await context.SaveChangesAsync();

        var summary = (await service.GetCourseSessionSummariesAsync(archived: false)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(summary.ActiveBookingCount, Is.EqualTo(expectedBooked));
            Assert.That(summary.AvailableSeats, Is.EqualTo(expectedAvailable));
            Assert.That(summary.BookingHistoryCount, Is.EqualTo(activeCount + 2));
        });
    }

    [Test]
    public async Task ArchiveCourseSessionAsync_WithoutHistoryIsRejected()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(2), 2, true);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await service.ArchiveCourseSessionAsync(session.Id));

        Assert.That(exception!.Message, Does.Contain("no booking history"));
        Assert.That((await context.CourseSessions.SingleAsync(x => x.Id == session.Id)).IsArchived, Is.False);
    }

    [Test]
    public async Task GetCourseSessionDetailsAsync_ReturnsEveryBookingAndCapacitySummary()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), 4, true);
        var active = CourseBooking.CreatePendingReservation(session.Id, "Anna Andersson", "anna@example.com", "0701111111");
        var cancelled = new CourseBooking(session.Id, "Bertil Berg", "bertil@example.com", "0702222222");
        cancelled.Cancel();
        context.CourseBookings.AddRange(active, cancelled);
        await context.SaveChangesAsync();

        var details = await service.GetCourseSessionDetailsAsync(session.Id);

        Assert.Multiple(() =>
        {
            Assert.That(details.Bookings, Has.Count.EqualTo(2));
            Assert.That(details.Capacity, Is.EqualTo(CourseSession.StandardCapacity));
            Assert.That(details.ActiveBookingCount, Is.EqualTo(1));
            Assert.That(details.AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity - 1));
            Assert.That(details.Bookings.Single(x => x.Id == active.Id).Status, Is.EqualTo(nameof(BookingStatus.Pending)));
            Assert.That(details.Bookings.Single(x => x.Id == cancelled.Id).Status, Is.EqualTo(nameof(BookingStatus.Cancelled)));
        });
    }

    [Test]
    public async Task CancelBookingAsync_CancelsAndRetainsBookingAndFreesCapacity()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), 1, true);
        var booking = CourseBooking.CreatePendingReservation(session.Id, "Cecilia Carlsson", "cecilia@example.com", "0703333333");
        context.CourseBookings.Add(booking);
        await context.SaveChangesAsync();

        var details = await service.CancelBookingAsync(session.Id, booking.Id);

        Assert.Multiple(() =>
        {
            Assert.That(details.ActiveBookingCount, Is.Zero);
            Assert.That(details.AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity));
            Assert.That(details.Bookings.Single().Status, Is.EqualTo(nameof(BookingStatus.Cancelled)));
        });
        Assert.That(await context.CourseBookings.AnyAsync(x => x.Id == booking.Id), Is.True);
    }

    [Test]
    public async Task CancelCourseSessionAsync_CancelsAllActiveBookingsAndMakesSessionUnbookable()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), 2, true);
        var pending = CourseBooking.CreatePendingReservation(session.Id, "David Dahl", "david@example.com", "0704444444");
        var confirmed = new CourseBooking(session.Id, "Erik Ek", "erik@example.com", "0705555555");
        var student = new StudentProfile("Erik", "Ek", "erik@example.com");
        context.StudentProfiles.Add(student);
        await context.SaveChangesAsync();
        confirmed.AssignStudentProfile(student.Id);
        confirmed.Confirm();
        context.CourseBookings.AddRange(pending, confirmed);
        await context.SaveChangesAsync();

        var details = await service.CancelCourseSessionAsync(session.Id);
        var storedSession = await context.CourseSessions.SingleAsync(x => x.Id == session.Id);

        Assert.Multiple(() =>
        {
            Assert.That(storedSession.IsCancelled, Is.True);
            Assert.That(storedSession.IsPublished, Is.False);
            Assert.That(details.IsCancelled, Is.True);
            Assert.That(details.ActiveBookingCount, Is.Zero);
            Assert.That(details.AvailableSeats, Is.Zero);
            Assert.That(details.Bookings, Has.All.Matches<CourseBookingAdminDetails>(x => x.Status == nameof(BookingStatus.Cancelled)));
        });
        Assert.That(await context.CourseBookings.CountAsync(x => x.CourseSessionId == session.Id), Is.EqualTo(2));

        var bookingService = new CourseBookingService(context);
        Assert.That(await bookingService.GetAvailableSeatsAsync(session.Id), Is.Zero);
        var bookingException = Assert.ThrowsAsync<CourseBookingException>(async () => await bookingService.CreatePendingReservationAsync(session.Id, "Frida F", "frida@example.com", "0706666666"));
        Assert.That(bookingException!.Error, Is.EqualTo(CourseBookingError.SessionCancelled));
    }

    [Test]
    public async Task BookStudentAsync_WithoutExistingAccount_CreatesConfirmedBookingAndProfile()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), CourseSession.StandardCapacity);

        var details = await service.BookStudentAsync(session.Id, "  Anna ", " Andersson ", "070 123 45 67", " ANNA@example.com ");
        var booking = await context.CourseBookings.SingleAsync();
        var profile = await context.StudentProfiles.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(details.ActiveBookingCount, Is.EqualTo(1));
            Assert.That(details.AvailableSeats, Is.EqualTo(15));
            Assert.That(details.Bookings.Single().Status, Is.EqualTo(nameof(BookingStatus.Confirmed)));
            Assert.That(details.Bookings.Single().FirstName, Is.EqualTo("Anna"));
            Assert.That(details.Bookings.Single().LastName, Is.EqualTo("Andersson"));
            Assert.That(booking.CustomerName, Is.EqualTo("Anna Andersson"));
            Assert.That(booking.StudentProfileId, Is.EqualTo(profile.Id));
            Assert.That(profile.UserId, Is.Null);
            Assert.That(context.Users, Is.Empty);
        });
    }

    [Test]
    public async Task BookStudentAsync_WhenStudentProfileExists_ReusesItCaseInsensitively()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanMC, "Riskettan MC", 3500m);
        context.Courses.Add(course);
        var profile = StudentProfile.CreateForStaffRegistration("Sara", "Svensson", "sara@example.com");
        context.StudentProfiles.Add(profile);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), CourseSession.StandardCapacity);

        await service.BookStudentAsync(session.Id, "Sara", "Svensson", "0701234567", "SARA@example.com");

        Assert.That(await context.StudentProfiles.CountAsync(), Is.EqualTo(1));
        Assert.That((await context.CourseBookings.SingleAsync()).StudentProfileId, Is.EqualTo(profile.Id));
    }

    [Test]
    public async Task BookStudentAsync_WhenStudentAlreadyHasActiveBooking_RejectsDuplicate()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), CourseSession.StandardCapacity);
        var existing = new CourseBooking(session.Id, "Different Recorded Name", "same@example.com", "0701111111");
        context.CourseBookings.Add(existing);
        await context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.BookStudentAsync(session.Id, "Same", "Student", "0702222222", "SAME@example.com"));

        Assert.That(exception!.Message, Does.Contain("already has an active booking"));
        Assert.That(await context.CourseBookings.CountAsync(), Is.EqualTo(1));
        Assert.That(await context.StudentProfiles.CountAsync(), Is.Zero);
    }

    [TestCase("not-an-email", "0701234567", "email")]
    [TestCase("valid@example.com", "not-a-phone", "phone")]
    [TestCase("valid@example.com", "0701234567890", "phone")]
    [TestCase("valid@example.com", "+47 912 34 567", "phone")]
    public async Task BookStudentAsync_InvalidContactDetailsAreRejected(string email, string phone, string expectedMessage)
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), CourseSession.StandardCapacity);

        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.BookStudentAsync(session.Id, "Anna", "Andersson", phone, email));

        Assert.That(exception!.Message, Does.Contain(expectedMessage));
        Assert.That(await context.CourseBookings.CountAsync(), Is.Zero);
        Assert.That(await context.StudentProfiles.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task BookStudentAsync_SeventeenthActiveBookingIsRejected()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), CourseSession.StandardCapacity);
        context.CourseBookings.AddRange(Enumerable.Range(0, CourseSession.StandardCapacity)
            .Select(index => new CourseBooking(session.Id, $"Student {index}", $"student{index}@example.com", "0701111111")));
        await context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.BookStudentAsync(session.Id, "Seventeenth", "Student", "0702222222", "student17@example.com"));

        Assert.That(exception!.Message, Does.Contain("course session is full"));
        Assert.That(await context.CourseBookings.CountAsync(), Is.EqualTo(CourseSession.StandardCapacity));
        Assert.That(await context.StudentProfiles.CountAsync(), Is.Zero);
    }

    [TestCase(true, false, "cancelled")]
    [TestCase(false, true, "archived")]
    public async Task BookStudentAsync_CancelledOrArchivedSessionIsRejected(bool cancelled, bool archived, string expectedMessage)
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), CourseSession.StandardCapacity);
        if (cancelled)
        {
            await service.CancelCourseSessionAsync(session.Id);
        }
        if (archived)
        {
            await service.BookStudentAsync(session.Id, "Prior", "Student", "0701111111", "prior@example.com");
            await service.ArchiveCourseSessionAsync(session.Id);
        }

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.BookStudentAsync(session.Id, "New", "Student", "0702222222", "new@example.com"));

        Assert.That(exception!.Message, Does.Contain(expectedMessage));
    }

    [Test]
    public async Task BookStudentAsync_CancelledBookingRemainsInHistoryAndFreesPlace()
    {
        await using var context = CreateContext();
        var service = new CourseSessionAdministrationService(context);
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m);
        context.Courses.Add(course);
        await context.SaveChangesAsync();
        var session = await service.CreateCourseSessionAsync(course.Id, DateTimeOffset.UtcNow.AddDays(4), DateTimeOffset.UtcNow.AddDays(4).AddHours(2), CourseSession.StandardCapacity);
        var booked = await service.BookStudentAsync(session.Id, "Eva", "Ek", "0701234567", "eva@example.com");

        var afterCancel = await service.CancelBookingAsync(session.Id, booked.Bookings.Single().Id);

        Assert.Multiple(() =>
        {
            Assert.That(afterCancel.ActiveBookingCount, Is.Zero);
            Assert.That(afterCancel.AvailableSeats, Is.EqualTo(CourseSession.StandardCapacity));
            Assert.That(afterCancel.Bookings, Has.Count.EqualTo(1));
            Assert.That(afterCancel.Bookings.Single().Status, Is.EqualTo(nameof(BookingStatus.Cancelled)));
        });
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
