namespace NyhemsTrafikskola.Domain;

public class CourseBooking
{
    private const int PendingReservationMinutes = 30;

    private CourseBooking()
    {
        CourseSessionId = Guid.Empty;
        CustomerName = string.Empty;
        Email = string.Empty;
        Phone = string.Empty;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = BookingStatus.Pending;
        ExpiresAt = null;
    }

    public CourseBooking(
        Guid courseSessionId,
        string customerName,
        string email,
        string phone,
        Guid? studentProfileId = null,
        DateTimeOffset createdAt = default,
        DateTimeOffset? expiresAt = null)
        : this()
    {
        CourseSessionId = courseSessionId;
        CustomerName = ValidateRequiredText(customerName, nameof(customerName));
        Email = ValidateRequiredText(email, nameof(email));
        Phone = ValidateRequiredText(phone, nameof(phone));
        StudentProfileId = studentProfileId;
        CreatedAt = createdAt == default ? DateTimeOffset.UtcNow : createdAt;
        Status = BookingStatus.Pending;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CourseSessionId { get; private set; }
    public Guid? StudentProfileId { get; private set; }
    public string CustomerName { get; private set; }
    public string Email { get; private set; }
    public string Phone { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsPendingReservationValid(DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        return Status == BookingStatus.Pending && (!ExpiresAt.HasValue || ExpiresAt.Value > now);
    }

    public bool CountsTowardCapacity(DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        return Status == BookingStatus.Confirmed || IsPendingReservationValid(now);
    }

    public static CourseBooking CreatePendingReservation(
        Guid courseSessionId,
        string customerName,
        string email,
        string phone,
        Guid? studentProfileId = null,
        DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new CourseBooking(
            courseSessionId,
            customerName,
            email,
            phone,
            studentProfileId,
            now,
            now.AddMinutes(PendingReservationMinutes));
    }

    public static CourseBooking CreatePendingReservation(
        Guid courseSessionId,
        string customerName,
        string email,
        string phone,
        Guid? studentProfileId,
        DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new CourseBooking(
            courseSessionId,
            customerName,
            email,
            phone,
            studentProfileId,
            now,
            expiresAt);
    }

    public void AssignStudentProfile(Guid studentProfileId)
    {
        if (studentProfileId == Guid.Empty)
        {
            throw new ArgumentException("Student profile id cannot be empty.", nameof(studentProfileId));
        }

        StudentProfileId = studentProfileId;
    }

    public void Confirm()
    {
        if (Status == BookingStatus.Cancelled)
        {
            throw new InvalidOperationException("A cancelled booking cannot be confirmed.");
        }

        if (Status == BookingStatus.Pending && ExpiresAt.HasValue && ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("An expired reservation cannot be confirmed.");
        }

        if (StudentProfileId is null || StudentProfileId == Guid.Empty)
        {
            throw new InvalidOperationException("A confirmed booking must be linked to a student profile.");
        }

        Status = BookingStatus.Confirmed;
        ExpiresAt = null;
    }

    public void Cancel()
    {
        if (Status == BookingStatus.Cancelled)
        {
            return;
        }

        Status = BookingStatus.Cancelled;
        ExpiresAt = null;
    }

    private static string ValidateRequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
