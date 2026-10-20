using SaveLocker.Shared;
using System.Diagnostics;

namespace SaveLocker.Agent;

/// <summary>
/// Which processes are each emulator SaveLocker reads (tasks/emulator-pull-safety Phase 3), on both OSes and from both
/// EmuDeck installs. A pull is refused while a game's emulator runs: card-based emulators (PCSX2, Dolphin's GCI folders,
/// DuckStation) hold the whole card in memory and write it back over a pulled save, and the maintainer chose to block
/// every emulator the same way (Q3, 2026-10-19). A new emulator gets a row here, as it gets one in the chip row.
/// <para>
/// A name matches whole (<c>.exe</c> ignored), or as its first 15 characters — Linux keeps only that many
/// (<c>/proc/&lt;pid&gt;/comm</c>), so under Proton <c>xenia_canary.exe</c> is <c>xenia_canary.ex</c>. A row with
/// <see cref="Row.Suffixed"/> also matches the name followed by <c>.</c>, <c>-</c> or <c>_</c> and anything: an AppImage
/// (<c>Eden.AppImage</c>), a build (<c>pcsx2-qtx64-avx2</c>, <c>duckstation-qt-x64-ReleaseLTCG</c>), a front end
/// (<c>yuzu-cmd</c>) — never a longer word (<c>EdenSync</c>, <c>xenia_manager</c> for Xenia, whose row is exact).
/// </para>
/// </summary>
public static class EmulatorProcesses
{
    public sealed record Row(string Emulator, IReadOnlyList<string> Names, bool Suffixed = true);

    public static readonly IReadOnlyList<Row> Rows =
    [
        new(RetroArchSaves.EmulatorName, ["retroarch"]),
        new(MelonDsSaves.EmulatorName, ["melonds"]),
        new(SupermodelSaves.EmulatorName, ["supermodel"]),
        // Model 2 Emulator is emulator.exe (or emulator_multicpu.exe) on Windows, and the same under Proton on the Deck.
        new(Model2Saves.EmulatorName, ["emulator", "emulator_multicpu"], Suffixed: false),
        new(ScummVmSaves.EmulatorName, ["scummvm"]),
        new(Pcsx2Saves.EmulatorName, ["pcsx2"]),
        new(DuckStationSaves.EmulatorName, ["duckstation"]),
        // EmuDeck runs Dolphin.exe on Windows and the dolphin-emu Flatpak on Linux; its PrimeHack is a Dolphin.exe too.
        new(DolphinSaves.EmulatorName, ["dolphin", "dolphin-emu"]),
        new(DolphinSaves.PrimeHackName, ["primehack", "dolphin", "dolphin-emu"]),
        new(PpssppSaves.EmulatorName, ["ppsspp", "ppssppwindows64", "ppssppwindows", "ppssppsdl", "ppssppqt"]),
        new(Rpcs3Saves.EmulatorName, ["rpcs3"]),
        new(Vita3KSaves.EmulatorName, ["vita3k"]),
        new(ShadPs4Saves.EmulatorName, ["shadps4", "shadps4qtlauncher"]),
        new(CemuSaves.EmulatorName, ["cemu"]),
        // Citra's folders are Azahar's (one game, Group E), and so is a running Citra.
        new(AzaharSaves.EmulatorName, ["azahar", "azahar-qt", "citra", "citra-qt"]),
        new(XeniaSaves.EmulatorName, ["xenia", "xenia_canary", "xenia_edge", "xenia-canary"], Suffixed: false),
        .. SwitchEmulators.Names.Select(n => new Row(n, [n.ToLowerInvariant()])),
    ];

    /// <summary>Is <paramref name="process"/> (a process name as .NET reads it) one of <paramref name="emulator"/>'s?</summary>
    public static bool IsProcessOf(string process, string emulator) =>
        Rows.Where(r => r.Emulator.Equals(emulator, StringComparison.OrdinalIgnoreCase))
            .Any(r => r.Names.Any(n => Matches(process, n, r.Suffixed)));

    /// <summary>Does <paramref name="process"/> match the process name <paramref name="name"/> (lower case, no <c>.exe</c>)?</summary>
    public static bool Matches(string process, string name, bool suffixed)
    {
        var p = process.ToLowerInvariant();
        var bare = p.EndsWith(".exe", StringComparison.Ordinal) ? p[..^4] : p;
        if (bare == name) return true;
        if (suffixed && bare.Length > name.Length && bare.StartsWith(name, StringComparison.Ordinal) && bare[name.Length] is '.' or '-' or '_')
            return true;
        return p.Length == 15 && ((name + ".exe").StartsWith(p, StringComparison.Ordinal) || name.StartsWith(p, StringComparison.Ordinal));
    }

    /// <summary>The emulators a game's saves come from: how this machine found it, and every emulator the server says
    /// any machine found it through. Empty for every other game.</summary>
    public static IReadOnlyList<string> EmulatorsOf(TrackedGame game) =>
        (game.Source is { Kind: GameSourceKinds.Emulator } source ? [source.Detail] : Array.Empty<string>())
            .Concat(game.Emulators)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The first of <paramref name="emulators"/> with a process among <paramref name="running"/>, with that
    /// process's name; null when none runs.</summary>
    public static (string Emulator, string Process)? Running(IEnumerable<string> emulators, IEnumerable<string> running)
    {
        var wanted = emulators.ToList();
        if (wanted.Count == 0) return null;
        foreach (var process in running)
            if (wanted.FirstOrDefault(e => IsProcessOf(process, e)) is { } emulator)
                return (emulator, process);
        return null;
    }

    /// <summary>The names of the processes running now. A process that exits while it is read is left out.</summary>
    public static IReadOnlyList<string> Snapshot()
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); }
            catch { /* it exited meanwhile */ }
            finally { p.Dispose(); }
        }
        return names;
    }
}
