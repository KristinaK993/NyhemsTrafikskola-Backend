using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Application.Bookings;

public interface ICourseBookingService
{
    Task<int> GetAvailableSeatsAsync(Guid courseSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CourseSessionAvailabilityDto>> GetPublishedFutureSessionsAsync(CourseType courseType, CancellationToken cancellationToken = default);
    Task<CreateBookingResult> CreatePendingBookingAsync(CreateBookingRequest request, CancellationToken cancellationToken = default);
    Task<CreateBookingResult> CreatePendingReservationAsync(Guid courseSessionId, string customerName, string email, string phone, Guid? studentProfileId = null, CancellationToken cancellationToken = default);
    Task<CreateBookingResult> CreateDirectConfirmedBookingAsync(Guid courseSessionId, Guid studentProfileId, string customerName, string email, string phone, CancellationToken cancellationToken = default);
    Task<CreateBookingResult> ConfirmPendingReservationAsync(Guid bookingId, Guid studentProfileId, CancellationToken cancellationToken = default);
}
