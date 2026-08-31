using ITBees.Alerts.Services;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

[TestFixture]
public class AlertTemplateRendererTests
{
    [Test]
    public void Placeholders_are_filled_from_the_event_values()
    {
        var result = AlertTemplateRenderer.Render("Disk {value}% on {source}",
            new Dictionary<string, string> { ["value"] = "95", ["source"] = "srv-01" });

        Assert.That(result, Is.EqualTo("Disk 95% on srv-01"));
    }

    [Test]
    public void Unknown_placeholders_are_left_visible()
    {
        var result = AlertTemplateRenderer.Render("Disk {value}% on {source}",
            new Dictionary<string, string> { ["value"] = "95" });

        Assert.That(result, Is.EqualTo("Disk 95% on {source}"),
            "a template typo must stay visible instead of rendering an empty sentence");
    }

    [Test]
    public void A_null_value_renders_as_empty()
    {
        var result = AlertTemplateRenderer.Render("Source: {source}.",
            new Dictionary<string, string> { ["source"] = null! });

        Assert.That(result, Is.EqualTo("Source: ."));
    }

    [Test]
    public void An_empty_value_set_returns_the_template_untouched()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlertTemplateRenderer.Render("{value}", new Dictionary<string, string>()),
                Is.EqualTo("{value}"));
            Assert.That(AlertTemplateRenderer.Render("{value}", null!), Is.EqualTo("{value}"));
            Assert.That(AlertTemplateRenderer.Render(null!, new Dictionary<string, string>()), Is.Null);
        });
    }

    [Test]
    public void Text_within_the_limit_is_returned_unchanged()
    {
        Assert.That(AlertTemplateRenderer.Shorten("short", 160), Is.EqualTo("short"));
    }

    [Test]
    public void Long_text_is_cut_to_the_limit()
    {
        var text = new string('a', 50) + " " + new string('b', 200);

        var result = AlertTemplateRenderer.Shorten(text, 160);

        Assert.That(result!.Length, Is.LessThanOrEqualTo(160));
    }

    [Test]
    public void Long_text_is_cut_on_a_word_boundary_when_one_is_near_the_end()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 60));

        var result = AlertTemplateRenderer.Shorten(text, 100);

        Assert.That(result, Does.StartWith("word word"));
        Assert.That(result!.TrimEnd('.', '…'), Does.Not.EndWith("wor"), "must not cut mid-word");
    }

    /// <summary>
    /// The SMS channel budgets 160 characters as one GSM-7 part. Every character the truncation
    /// marker adds must therefore be GSM-7 encodable, otherwise the provider silently switches
    /// the whole message to UCS-2 (70 characters per part) and bills three parts instead of one.
    /// </summary>
    [Test]
    public void The_truncation_marker_stays_within_the_GSM7_alphabet()
    {
        // GSM 03.38 basic alphabet plus its extension table, written verbatim so no C#
        // escape sequence can quietly alter the set under test.
        const string gsm7Printable =
            @"@£$¥èéùìòÇØøÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !""#¤%&'()*+,-./0123456789:;<=>?"
            + @"¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà"
            + @"^{}\[~]|€";
        var gsm7 = new HashSet<char>(gsm7Printable) { '\n', '\r', '\f' };

        var result = AlertTemplateRenderer.Shorten(new string('a', 400), 160);

        var offenders = result!.Where(c => !gsm7.Contains(c)).Distinct().ToList();
        Assert.That(offenders, Is.Empty,
            "non-GSM-7 characters force UCS-2 encoding: " +
            string.Join(", ", offenders.Select(c => $"U+{(int)c:X4}")));
    }

    [Test]
    public void A_cut_is_marked_so_the_reader_knows_text_is_missing()
    {
        var result = AlertTemplateRenderer.Shorten(new string('a', 400), 160);

        Assert.That(result, Is.Not.EqualTo(new string('a', 160)),
            "a silent cut hides from the reader that the message was truncated");
    }
}
