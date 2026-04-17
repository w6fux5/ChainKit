namespace ChainKit.Evm.Watching;

/// <summary>
/// Event args for <see cref="EvmNodeHealthWatcher.OnHealthChecked"/>.
/// </summary>
public sealed class EvmNodeHealthCheckedEventArgs : EventArgs
{
    /// <summary>The report produced by this poll.</summary>
    public EvmNodeHealthReport Report { get; }

    /// <summary>Creates event args wrapping the given report.</summary>
    public EvmNodeHealthCheckedEventArgs(EvmNodeHealthReport report)
    {
        Report = report;
    }
}
