namespace ChainKit.Tron.Watching;

/// <summary>
/// Event args for <see cref="TronNodeHealthWatcher.OnHealthChecked"/>.
/// </summary>
public sealed class TronNodeHealthCheckedEventArgs : EventArgs
{
    /// <summary>The report produced by this poll.</summary>
    public TronNodeHealthReport Report { get; }

    /// <summary>Creates event args wrapping the given report.</summary>
    public TronNodeHealthCheckedEventArgs(TronNodeHealthReport report)
    {
        Report = report;
    }
}
