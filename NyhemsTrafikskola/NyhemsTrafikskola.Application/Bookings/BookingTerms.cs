namespace NyhemsTrafikskola.Application.Bookings;

public static class BookingTerms
{
    // Change this together with the terms text in the frontend (RiskettanBookingForm) whenever the wording changes.
    public const string CurrentVersion = "2026-10-14";
}

// Who created a booking and, for online bookings, which terms were accepted and when.
public sealed record BookingAudit(string? CreatedByUserId, string? TermsVersion = null, DateTimeOffset? TermsAcceptedAt = null);
