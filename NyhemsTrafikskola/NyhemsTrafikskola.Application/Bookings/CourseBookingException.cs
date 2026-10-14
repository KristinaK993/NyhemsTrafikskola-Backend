namespace NyhemsTrafikskola.Application.Bookings;

public enum CourseBookingError
{
    SessionNotFound,
    SessionArchived,
    SessionCancelled,
    SessionNotPublished,
    SessionStarted,
    AlreadyBooked,
    SessionFull,
    BookingNotFound,
    BookingAlreadyCancelled,
    CancellationDeadlinePassed,
}

public sealed class CourseBookingException : InvalidOperationException
{
    public CourseBookingException(CourseBookingError error, string message)
        : base(message)
    {
        Error = error;
    }

    public CourseBookingError Error { get; }
}
