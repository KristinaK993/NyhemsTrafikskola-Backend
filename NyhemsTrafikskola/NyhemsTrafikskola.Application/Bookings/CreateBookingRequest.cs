namespace NyhemsTrafikskola.Application.Bookings;

public sealed record CreateBookingRequest(
    Guid CourseSessionId,
    string CustomerName,
    string Email,
    string Phone,
    Guid? StudentProfileId = null);
