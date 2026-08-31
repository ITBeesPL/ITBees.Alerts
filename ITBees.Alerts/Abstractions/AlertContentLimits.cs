namespace ITBees.Alerts.Abstractions;

/// <summary>
/// The single home for content limits. The database column widths in
/// <c>Setup.DbModelBuilder</c> and the publisher's payload normalisation both read from here,
/// so a limit can only be changed in one place.
/// </summary>
public static class AlertContentLimits
{
    public const int Title = 400;
    public const int Body = 8192;
    public const int ValuesJson = 16384;
    public const int Link = 2048;

    public const int SourceId = 128;
    public const int SourceName = 200;

    /// <summary>Placeholder name of one entry in <c>AlertEvent.Values</c>.</summary>
    public const int ValueKey = 128;

    /// <summary>How many <c>AlertEvent.Values</c> entries are carried at most.</summary>
    public const int ValueEntries = 100;
}
