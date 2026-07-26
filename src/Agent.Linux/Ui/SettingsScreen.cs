using System.Numerics;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// The Settings screen — the agent UI's <c>SettingsView.tsx</c>, minus everything that needs a
/// keyboard.
///
/// Game Mode has no text entry: <see cref="UiApp"/> deliberately suppresses the on-screen keyboard
/// (gamescope pops it for any client with SDL text input active, and it covers the UI). So the
/// connection fields are shown read-only and point at Desktop Mode for edits, while the two
/// settings that can be driven with a D-pad — the settle gate and auto-start — are editable here.
/// </summary>
sealed class SettingsScreen
{
    private readonly AgentConfig _config;
    private readonly IAutoStart _autoStart;

    private int _settleSeconds;
    private bool _autoStartOn;
    private bool _soundsOn;
    private bool _loaded;
    private string _status = "";
    private readonly HashSet<string> _toRemove = new();

    public SettingsScreen(AgentConfig config, IAutoStart autoStart)
    {
        _config = config;
        _autoStart = autoStart;
    }

    /// <summary>
    /// Read the live values once. Auto-start in particular is a systemd query, so it must not run
    /// every frame — an immediate-mode screen would otherwise shell out 60 times a second.
    /// </summary>
    private void EnsureLoaded()
    {
        if (_loaded) return;
        _settleSeconds = _config.SettleQuietSeconds;
        _soundsOn = !_config.UiSoundsMuted;
        try { _autoStartOn = _autoStart.IsEnabled(); } catch { _autoStartOn = false; }
        _loaded = true;
    }

    public void Draw()
    {
        EnsureLoaded();

        Widgets.Text("Settings", Theme.TextPrimary, Theme.Title);
        Widgets.Gap(Theme.Space.Sm);

        Widgets.TwoColumn("settings", 0.5f, DrawLeft, DrawRight);
    }

    private void DrawLeft()
    {
        Widgets.SectionHeader("Connection");
        InfoRow("Server", _config.ServerUrl ?? "-", mono: true);
        InfoRow("Machine", _config.MachineName ?? "-");
        InfoRow("Status", string.IsNullOrEmpty(_config.ApiKey) ? "Not enrolled" : "Enrolled",
            colour: string.IsNullOrEmpty(_config.ApiKey) ? Theme.AccentAmber : Theme.AccentGreen);

        Widgets.Gap(Theme.Space.Sm);
        Widgets.TextWrapped(
            "These are set during enrolment. Game Mode has no keyboard, so change them from Desktop "
            + "Mode with  savelocker set-server  or in the web console.",
            Theme.TextDim, Theme.Caption);

        Widgets.SectionHeader("Sync safety");
        Widgets.TextWrapped(
            "How long SaveLocker waits for a game to stop writing before it uploads a save. "
            + "Raise it if a game flushes slowly on exit.",
            Theme.TextMuted, Theme.Caption);
        Widgets.Gap(Theme.Space.Sm);

        if (Widgets.Stepper("seconds", ref _settleSeconds, 0, 120, 1))
        {
            // UpdateSettings, not Save: this process loaded config.json when the UI opened and has
            // never reloaded it, so a whole-object write would put a stale game list back on disk.
            var seconds = _settleSeconds;
            _config.UpdateSettings(c => c.SettleQuietSeconds = seconds);
            _status = $"Settle gate set to {_settleSeconds}s.";
        }

        Widgets.SectionHeader("Interface");

        if (Widgets.Toggle("Interface sounds", ref _soundsOn))
        {
            Sound.Muted = !_soundsOn;
            var muted = !_soundsOn;
            _config.UpdateSettings(c => c.UiSoundsMuted = muted);
            _status = _soundsOn ? "Interface sounds on." : "Interface sounds muted.";

            // The toggle's own click already fired inside Widgets.Toggle, while sounds were still
            // muted — so switching them ON would otherwise be silent, which reads as "it didn't
            // work". Confirm it here, now that the mute is lifted.
            if (_soundsOn) Sound.Play(Sound.Cue.Toggle);
        }

        Widgets.TextWrapped(
            Sound.Available
                ? $"Navigation and selection feedback. Source: {Sound.Source}."
                : $"Unavailable on this machine ({Sound.Unavailable ?? "audio not started"}), "
                  + "so nothing will play.",
            Theme.TextDim, Theme.Caption);

        Widgets.SectionHeader("Startup");
        var was = _autoStartOn;
        if (Widgets.Toggle("Start SaveLocker on boot", ref _autoStartOn) && was != _autoStartOn)
        {
            // Reflect what the platform actually did, not what was asked: SetEnabled returns false
            // when systemd --user is unavailable, and a toggle that lies is worse than one that
            // refuses.
            var applied = false;
            try { applied = _autoStart.SetEnabled(_autoStartOn); } catch { /* reported below */ }
            if (!applied)
            {
                _autoStartOn = was;
                _status = "Could not change auto-start (is systemd --user available?).";
            }
            else
            {
                _status = _autoStartOn ? "Auto-start enabled." : "Auto-start disabled.";
            }
        }
        Widgets.TextWrapped("Runs the sync daemon in the background via a systemd --user unit.",
            Theme.TextDim, Theme.Caption);

        if (!string.IsNullOrEmpty(_status))
        {
            Widgets.Gap(Theme.Space.Md);
            Widgets.Text(_status, Theme.AccentGreen, Theme.Caption);
        }
    }

    private void DrawRight()
    {
        Widgets.SectionHeader($"Tracked games ({_config.Games.Count})");

        if (_config.Games.Count == 0)
        {
            Widgets.Text("No games tracked yet.", Theme.TextMuted);
            return;
        }

        var avail = ImGui.GetContentRegionAvail().Y;
        var barH = ImGui.GetTextLineHeight() + (Theme.Space.Sm + 2f) * 2
                   + ImGui.GetStyle().ItemSpacing.Y * 2 + Theme.Space.Sm;
        // AlwaysUseWindowPadding: without it this child has ZERO padding and the rows' focus rings are
        // clipped at both edges (Theme.Layout.FocusClearance).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(Theme.Layout.FocusClearance, Theme.Layout.FocusClearance));
        ImGui.BeginChild("games", new Vector2(0, MathF.Max(120f, avail - barH)),
            ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NavFlattened);
        NavDebug.PushScope("games");

        foreach (var g in _config.Games.ToList())
        {
            var marked = _toRemove.Contains(g.Name);
            var missing = string.IsNullOrEmpty(g.SaveDirectory);

            if (Widgets.ListRow($"tg{g.Name}", g.Name,
                    missing ? "No save folder set" : g.SaveDirectory,
                    missing ? Icons.AlertTriangle : Icons.Folder,
                    trailing: marked ? "will be removed" : null,
                    trailingColour: marked ? Theme.AccentAmber : null,
                    chevron: false,
                    selected: marked))
            {
                // The row toggles a mark rather than deleting on the spot: on a gamepad an
                // accidental A press is easy, and untracking a game silently is not recoverable
                // from this screen.
                if (marked) _toRemove.Remove(g.Name); else _toRemove.Add(g.Name);
            }
        }

        NavDebug.PopScope();
        ImGui.EndChild();
        NavDebug.NoteContainer("games");
        ImGui.PopStyleVar();
        Widgets.Gap(Theme.Space.Sm);

        var label = _toRemove.Count > 0 ? $"Remove {_toRemove.Count} selected" : "Remove selected";
        if (Widgets.PillButton(label, Widgets.ButtonKind.Danger, Icons.Trash,
                enabled: _toRemove.Count > 0))
        {
            // SetTracked, not RemoveAll + Save: a plain local delete does not stick. The game is
            // still on the server, so the running daemon's next reconcile adopts it right back —
            // and this screen would have told the user it was removed.
            var doomed = _config.Games.Where(g => _toRemove.Contains(g.Name)).Select(g => g.GameId).ToList();
            foreach (var id in doomed) _config.SetTracked(id, tracked: false);
            var removed = doomed.Count;
            _status = $"Removed {removed} game(s) from this device.";
            _toRemove.Clear();
        }

        if (_toRemove.Count > 0)
        {
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.AlignTextToFramePadding();
            Widgets.Text("Stops syncing here. Saves already on the server are kept.",
                Theme.TextMuted, Theme.Caption);
        }
    }

    private static void InfoRow(string label, string value, bool mono = false, Vector4? colour = null)
    {
        Widgets.Text(label, Theme.TextMuted, Theme.Caption);
        ImGui.SameLine(96f);
        Widgets.Text(value, colour ?? Theme.TextPrimary, mono ? Theme.Mono : Theme.Body);
    }
}
