using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Tests;

public class RiskettanDomainTests
{
    [Test]
    public void Course_WhenValid_IsCreatedAndCanBeToggled()
    {
        var course = new Course(CourseType.RiskettanBil, "Riskettan Bil", 3200m, isActive: true);

        Assert.Multiple(() =>
        {
            Assert.That(course.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(course.Type, Is.EqualTo(CourseType.RiskettanBil));
            Assert.That(course.Title, Is.EqualTo("Riskettan Bil"));
            Assert.That(course.Price, Is.EqualTo(3200m));
            Assert.That(course.IsActive, Is.True);
        });

        course.Deactivate();
        Assert.That(course.IsActive, Is.False);

        course.Activate();
        Assert.That(course.IsActive, Is.True);
    }

    [TestCase(CourseType.RiskettanBil, "", 3200)]
    [TestCase(CourseType.RiskettanMC, "   ", 3200)]
    [TestCase(CourseType.RiskettanBil, "Riskettan Bil", -10)]
    public void Course_WhenInvalid_Throws(CourseType type, string title, int price)
    {
        if (price < 0)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Course(type, title, price));
            return;
        }

        Assert.Throws<ArgumentException>(() => new Course(type, title, price));
    }

    [Test]
    public void CourseSession_WhenValid_IsCreatedAndCanBePublished()
    {
        var start = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(2);

        var session = new CourseSession(Guid.NewGuid(), start, end, 12, isPublished: false);

        Assert.Multiple(() =>
        {
            Assert.That(session.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(session.CourseId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(session.StartTime, Is.EqualTo(start));
            Assert.That(session.EndTime, Is.EqualTo(end));
            Assert.That(session.Capacity, Is.EqualTo(12));
            Assert.That(session.IsPublished, Is.False);
        });

        session.Publish();
        Assert.That(session.IsPublished, Is.True);

        session.Unpublish();
        Assert.That(session.IsPublished, Is.False);
    }

    [TestCase(10, 0)]
    [TestCase(10, -10)]
    [TestCase(0, 20)]
    public void CourseSession_WhenInvalid_Throws(int capacity, int minutes)
    {
        var start = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(minutes);

        if (capacity <= 0)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CourseSession(Guid.NewGuid(), start, end, capacity));
            return;
        }

        Assert.Throws<ArgumentException>(() => new CourseSession(Guid.NewGuid(), start, end, capacity));
    }

    [Test]
    public void CourseBooking_WhenCreated_HasPendingStatus()
    {
        var booking = new CourseBooking(
            Guid.NewGuid(),
            "Anna Andersson",
            "anna@example.com",
            "0701234567",
            studentProfileId: null);

        Assert.Multiple(() =>
        {
            Assert.That(booking.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(booking.Status, Is.EqualTo(BookingStatus.Pending));
            Assert.That(booking.CreatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
            Assert.That(booking.StudentProfileId, Is.Null);
        });
    }

    [Test]
    public void CourseBooking_WhenStudentProfileIsAssigned_ThenConfirmAndCancel_UpdatesStatus()
    {
        var studentProfileId = Guid.NewGuid();
        var booking = new CourseBooking(
            Guid.NewGuid(),
            "Anna Andersson",
            "anna@example.com",
            "0701234567");

        booking.AssignStudentProfile(studentProfileId);
        booking.Confirm();
        Assert.That(booking.Status, Is.EqualTo(BookingStatus.Confirmed));

        booking.Cancel();
        Assert.That(booking.Status, Is.EqualTo(BookingStatus.Cancelled));
    }

    [Test]
    public void CourseBooking_WhenConfirmedWithoutStudentProfile_Throws()
    {
        var booking = new CourseBooking(
            Guid.NewGuid(),
            "Anna Andersson",
            "anna@example.com",
            "0701234567");

        Assert.Throws<InvalidOperationException>(() => booking.Confirm());
    }

    [Test]
    public void CourseBooking_WhenCancelled_CannotBeConfirmedAgain()
    {
        var booking = new CourseBooking(
            Guid.NewGuid(),
            "Anna Andersson",
            "anna@example.com",
            "0701234567",
            Guid.NewGuid());

        booking.Cancel();
        Assert.That(booking.Status, Is.EqualTo(BookingStatus.Cancelled));

        Assert.Throws<InvalidOperationException>(() => booking.Confirm());
    }

    [TestCase("", "anna@example.com", "0701234567")]
    [TestCase("Anna Andersson", "", "0701234567")]
    [TestCase("Anna Andersson", "anna@example.com", "")]
    public void CourseBooking_WhenContactInformationIsInvalid_Throws(string name, string email, string phone)
    {
        Assert.Throws<ArgumentException>(() => new CourseBooking(
            Guid.NewGuid(),
            name,
            email,
            phone));
    }
}
