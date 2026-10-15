namespace NyhemsTrafikskola.Application.Validation;

// Accepts Swedish phone numbers written nationally (070-123 45 67) or with +46 / 0046.
// The number is validated in its national form: a leading 0 followed by 6–9 digits (7–10 digits in total).
public static class SwedishPhoneNumber
{
    public const int MaxLength = 50;
    private const int MinNationalDigits = 7;
    private const int MaxNationalDigits = 10;

    public static bool IsValid(string? value)
    {
        var national = ToNationalDigits(value);
        return national is not null
            && national.Length is >= MinNationalDigits and <= MaxNationalDigits
            && national[0] == '0'
            && national[1] != '0';
    }

    private static string? ToNationalDigits(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return null;
        }

        var hasPlusPrefix = trimmed[0] == '+';
        var body = hasPlusPrefix ? trimmed[1..] : trimmed;
        if (body.Any(character => !char.IsAsciiDigit(character) && character is not (' ' or '-' or '(' or ')')))
        {
            return null;
        }

        var digits = new string(body.Where(char.IsAsciiDigit).ToArray());
        string? subscriberDigits = null;

        if (hasPlusPrefix)
        {
            if (!digits.StartsWith("46", StringComparison.Ordinal))
            {
                return null;
            }

            subscriberDigits = digits[2..];
        }
        else if (digits.StartsWith("0046", StringComparison.Ordinal))
        {
            subscriberDigits = digits[4..];
        }

        if (subscriberDigits is null)
        {
            return digits.Length >= 2 ? digits : null;
        }

        // "+46 (0)70 ..." is a common way of writing the number; the extra 0 is ignored.
        subscriberDigits = subscriberDigits.StartsWith('0') ? subscriberDigits[1..] : subscriberDigits;
        return subscriberDigits.Length >= 1 ? "0" + subscriberDigits : null;
    }
}
