namespace NyhemsTrafikskola.Domain;

public class CourseBooking
{
    private CourseBooking()
    {
        CourseSessionId = Guid.Empty;
        CustomerName = string.Empty;
        Email = string.Empty;
        Phone = string.Empty;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = BookingStatus.Pending;
    }

    public CourseBooking(
        Guid courseSessionId,
        string customerName,
        string email,
        string phone,
        Guid? studentProfileId = null,
        DateTimeOffset createdAt = default)
        : this()
    {
        CourseSessionId = courseSessionId;
        CustomerName = ValidateRequiredText(customerName, nameof(customerName));
        Email = ValidateRequiredText(email, nameof(email));
        Phone = ValidateRequiredText(phone, nameof(phone));
        StudentProfileId = studentProfileId;
        CreatedAt = createdAt == default ? DateTimeOffset.UtcNow : createdAt;
        Status = BookingStatus.Pending;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CourseSessionId { get; private set; }
    public Guid? StudentProfileId { get; private set; }
    public string CustomerName { get; private set; }
    public string Email { get; private set; }
    public string Phone { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

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

        if (StudentProfileId is null || StudentProfileId == Guid.Empty)
        {
            throw new InvalidOperationException("A confirmed booking must be linked to a student profile.");
        }

        Status = BookingStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status == BookingStatus.Cancelled)
        {
            return;
        }

        Status = BookingStatus.Cancelled;
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
