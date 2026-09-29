using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Application.Bookings;

public sealed record CreateBookingResult(
    Guid BookingId,
    Guid CourseSessionId,
    BookingStatus Status,
    int AvailableSeats);
