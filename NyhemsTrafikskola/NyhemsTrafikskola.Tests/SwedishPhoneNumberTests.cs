using NyhemsTrafikskola.Application.Validation;

namespace NyhemsTrafikskola.Tests;

public class SwedishPhoneNumberTests
{
    [TestCase("0701234567")]
    [TestCase("070-123 45 67")]
    [TestCase("070 123 45 67")]
    [TestCase("08-123 456")]
    [TestCase("08-12 34 56 7")]
    [TestCase("031-123 45 67")]
    [TestCase("+46701234567")]
    [TestCase("+46 70-123 45 67")]
    [TestCase("+46 (0)70 123 45 67")]
    [TestCase("0046 70 123 45 67")]
    public void IsValid_WhenNumberIsSwedish_ReturnsTrue(string phoneNumber)
    {
        Assert.That(SwedishPhoneNumber.IsValid(phoneNumber), Is.True);
    }

    [TestCase("0701234567890")]
    [TestCase("07012345678")]
    [TestCase("+46 70 123 45 678")]
    [TestCase("12345")]
    [TestCase("701234567")]
    [TestCase("0012345678")]
    [TestCase("+47 912 34 567")]
    [TestCase("+1 202 555 0123")]
    [TestCase("070-ring-mig")]
    [TestCase("070+1234567")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void IsValid_WhenNumberIsNotAValidSwedishNumber_ReturnsFalse(string? phoneNumber)
    {
        Assert.That(SwedishPhoneNumber.IsValid(phoneNumber), Is.False);
    }
}
