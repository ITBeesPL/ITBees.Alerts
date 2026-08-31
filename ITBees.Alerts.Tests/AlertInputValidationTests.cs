using ITBees.Alerts.Services;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

[TestFixture]
public class AlertInputValidationTests
{
    [TestCase("ops@example.com", true)]
    [TestCase("first.last+tag@sub.example.co.uk", true)]
    [TestCase("Ops <ops@example.com>", false, Description = "display-name form must not pass as an address")]
    [TestCase("not-an-address", false)]
    [TestCase("ops@example.com\r\nBcc: attacker@evil.com", false, Description = "header injection")]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void Email_addresses_are_validated(string? value, bool expected)
    {
        Assert.That(AlertInputValidation.IsEmailAddress(value), Is.EqualTo(expected));
    }

    [Test]
    public void An_over_long_email_is_rejected()
    {
        var value = new string('a', 320) + "@example.com";

        Assert.That(AlertInputValidation.IsEmailAddress(value), Is.False);
    }

    [TestCase("https://example.com/alerts", true)]
    [TestCase("http://example.com", true)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("file:///etc/passwd", false)]
    [TestCase("/relative/path", false)]
    [TestCase(null, false)]
    public void Only_http_and_https_links_are_accepted(string? value, bool expected)
    {
        Assert.That(AlertInputValidation.IsWebLink(value), Is.EqualTo(expected));
    }
}
