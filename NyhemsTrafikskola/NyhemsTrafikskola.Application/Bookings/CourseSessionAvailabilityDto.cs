using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Application.Bookings;

public sealed record CourseSessionAvailabilityDto(
    Guid CourseSessionId,
    Guid CourseId,
    CourseType CourseType,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int Capacity,
    int AvailableSeats);
