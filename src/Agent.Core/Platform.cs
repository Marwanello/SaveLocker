namespace SaveLocker.Agent;

/// <summary>
/// Launch-on-login toggle. Windows implements it with the HKCU Run key; a Linux
/// agent will implement it with a systemd --user unit. Core only needs the toggle.
/// </summary>
public interface IAutoStart
{
    /// <summary>
    /// True only when the effective login entry would start <b>this</b> agent. A leftover entry from
    /// an install that has since moved or been removed is not "enabled" — reporting it as such is
    /// what let the UI claim a setting the machine was not honouring (WA-10).
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Add or remove the login entry. The reason matters as much as the outcome: the caller shows it
    /// to the user, and "it didn't work" with no explanation is indistinguishable from a UI bug.
    /// </summary>
    AutoStartResult SetEnabled(bool enabled);
}

/// <summary>
/// The outcome of an auto-start change, with a reason a person can act on when it failed —
/// group policy, a locked registry key, no user bus on a headless Deck.
/// </summary>
public readonly record struct AutoStartResult(bool Ok, string? Error)
{
    public static AutoStartResult Success() => new(true, null);
    public static AutoStartResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// Local game discovery. The sources are entirely platform-specific (Windows reads the
/// registry, Steam libraries and known folders; Linux reads shortcuts.vdf and Proton
/// prefixes), so Core consumes only the resulting candidates.
/// </summary>
public interface IGameScanner
{
    Task<IReadOnlyList<ScanCandidate>> ScanAsync(CancellationToken ct = default);
}
