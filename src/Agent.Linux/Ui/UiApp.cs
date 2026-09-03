using System.Net.Http.Json;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using ImGuiNET;
using SaveLocker.Shared;
using SdlApi = Silk.NET.SDL.Sdl;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// `savelocker ui` — the gamepad-native Game Mode surface (Linux-Agent-Streamline.md §3). All three
/// on-device gates passed 2026-07-24: renders under gamescope (§3.3.1), Steam Input delivers a
/// gamepad (§3.3.2), tarball delta is single-digit MB (§3.1).
///
/// It is a *view*, not a second frontend: it drives the same Agent.Core services the daemon's HTTP
/// API wraps (<see cref="LinuxGameScanner"/>, <see cref="PathBrowser"/>, <see cref="Enroller"/>,
/// <see cref="AgentConfig"/>) in-process — no second API client, no duplicated sync logic.
/// </summary>
sealed class UiApp
{
    private enum Screen { Status, AddGame, SetFolder, LaunchSetup, Conflicts, Settings, Gallery }

    private readonly AgentConfig _config;
    private readonly LinuxGameScanner _scanner;
    private readonly PathBrowser _browser;
    private readonly LaunchCommandDto _launch;
    private readonly SettingsScreen _settings;
    private readonly LeaseWarningStore _leaseWarnings;

    // Warnings are re-read on a timer rather than every frame: the daemon and the launch wrapper
    // write this file from other processes, so it has to be polled, but it changes at most twice
    // per game launch and a per-frame read would hit the disk 60 times a second.
    private IReadOnlyList<LeaseWarningStore.Entry> _warnings = Array.Empty<LeaseWarningStore.Entry>();
    private long _warningsReadAt;
    private const long WarningsPollMs = 2000;

    // Same reasoning as the lease warnings above, on a shorter poll: this is what is driving a
    // progress bar, and the daemon — a separate process, actually doing the syncing — is the only
    // thing that knows the byte counts (SyncActivityStore).
    private readonly SyncActivityStore _activityStore;
    private SyncActivitySnapshot? _activityCurrent;
    private IReadOnlyList<ActivityLogEntry> _activityRecent = Array.Empty<ActivityLogEntry>();
    private long _activityReadAt;
    private const long ActivityPollMs = 400;

    // "Sync now": the one action this otherwise read-only screen may still take, because it does not
    // do the syncing itself — it asks the daemon's own local API to, the same call the browser UI's
    // button makes. _apiPort/_localAuth are what let this process (a separate one from the daemon)
    // reach and authenticate to it.
    private readonly int _apiPort;
    private readonly LocalAuth _localAuth;
    private Task<string>? _syncNowTask;
    private string? _syncNowMessage;

    // Conflicts (tasks/conflict-resolution-ui/plan.md, Phase 8). Unlike the "Sync now" call above,
    // resolving a conflict never touches the daemon's live SyncEngine/lease state — it is the same
    // stateless, mechanical server call AgentCli.cs's `conflicts`/`resolve-conflict` commands already
    // make — so this talks to the remote server directly through Agent.Core's own ApiClient,
    // in-process, rather than adding a second HTTP hop through the daemon's local API.
    //
    // Built fresh per call, not cached: every other ApiClient.For(_config) call site (AgentCli.cs's
    // Api(), AgentApiServer.cs's route handlers) does the same, so a config identity change made
    // elsewhere in the process is always picked up on the next call.
    private ApiClient Api() => ApiClient.For(_config);

    // Polled on a timer, not per frame: unlike the lease-warning/activity files this is a real
    // network round trip. Filtered to conflicts THIS machine is a party to — same "no bystander
    // case" rule AgentCli.cs's `conflicts` command and Doctor.cs already apply (plan.md decision 2).
    private List<ConflictDto> _openConflicts = new();
    private Task<List<ConflictDto>>? _conflictsTask;
    private long _conflictsReadAt;
    private const long ConflictsPollMs = 12000;
    private string? _conflictsError;

    // A conflict only carries version ids; both sides' machine/timestamp/size and file-count are
    // fetched lazily and cached forever (an archive's stats never change once uploaded), mirroring
    // agent-ui's useConflictVersions.ts. Every field write happens on the render thread, in
    // PollConflictState — never inside the async continuation itself — the same discipline
    // _scanTask/_enrollTask already follow, so there is no cross-thread field mutation here.
    // All five caches below are pruned in PollConflictState whenever a conflict or version they
    // reference is no longer open, regardless of which surface (this screen, the CLI, or the
    // dashboard) actually resolved it — otherwise they would grow for the life of the process.
    private readonly Dictionary<Guid, SaveVersionDto> _versions = new();
    private readonly Dictionary<Guid, VersionStatsDto> _versionStats = new();
    private readonly Dictionary<Guid, Task<SaveVersionDto?>> _versionTasks = new();
    private readonly Dictionary<Guid, Task<VersionStatsDto?>> _versionStatsTasks = new();
    // A failed/missing fetch is retried on a cooldown, not every frame — set whenever a version or
    // stats task completes without a result (404, or any other failure).
    private readonly Dictionary<Guid, long> _versionRetryAt = new();
    private const long VersionRetryCooldownMs = 10000;

    private readonly Dictionary<Guid, bool> _conflictKeepBoth = new();
    private Guid? _resolvingConflictId;
    private ConflictDto? _resolvingConflict;
    private Guid? _resolvingWinningVersionId;
    private Task<(bool ok, string? error)>? _resolveTask;
    private string? _resolveError;

    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController _controller = null!;
    private readonly HashSet<int> _hookedPads = new();

    // Nav is driven by ButtonDown EVENTS, not a per-frame poll: at 30 fps a quick D-pad tap can
    // begin and end inside one frame, so polling drops it and nav feels dead. Events never miss.
    // Each queued press is replayed as a single-frame ImGui key pulse — reliable and flicker-free.
    private readonly object _navLock = new();
    private readonly List<ImGuiKey> _navQueue = new();
    private ImGuiKey[] _navDownLastFrame = Array.Empty<ImGuiKey>();
    // Held D-pad directions, for hold-to-repeat scrolling: key -> when pressed / when last repeated.
    private readonly Dictionary<ImGuiKey, long> _heldSince = new();
    private readonly Dictionary<ImGuiKey, long> _lastRepeat = new();
    private const long RepeatDelayMs = 400;
    private const long RepeatRateMs = 90;

    private Screen _screen = Screen.Status;

    // Cross-fade state. _renderedScreen lags _screen by exactly the frame in which navigation
    // happened, which is how the change is detected without every caller having to announce it.
    private Screen _renderedScreen = Screen.Status;
    private float _screenFade = 1f;
    // 0.30 s, up from 0.16. Ease-out cubic spends most of its time near full opacity, so at 160 ms the
    // visible part of the transition was roughly 80 ms — it measured as working and went unnoticed on
    // the Deck across a whole session, same failure as the focus ring's alpha-only pulse.
    private const float ScreenFadeSeconds = 0.30f;

    /// <summary>Put the nav cursor on the rail on the first frame, so the D-pad works immediately.</summary>
    private bool _focusRailOnce = true;

    // Which pane owns the cursor. Only that pane is navigable, so a directional move can never
    // wander across the boundary by accident; crossing panes is always deliberate.
    private enum Zone { Rail, Content }
    private Zone _focusZone = Zone.Rail;

    private uint _activeRailId;        // rail entry for the screen being shown
    private uint _bestContentId;       // topmost-leftmost focusable control in the content pane
    // Right pressed while the content had nothing focusable yet — a scan still running, so the pane
    // is just a spinner. The intent is held until a control appears rather than being swallowed.
    private int _pendingCrossFrames;
    private const int PendingCrossFrames = 1800;   // ~30 s; a cold scan can take a while
    private bool _navLeftFired, _navRightFired, _navBackFired;
    private int _strandedFrames;

    // Scripted navigation, for verification. Gamepad nav is the single hardest thing to test off a
    // Deck, and it is exactly where the bugs are — so `--nav right,down,a` replays presses through
    // the same queue a real pad feeds, and `--screenshot` then captures where the cursor landed.
    private readonly Queue<ImGuiKey> _navScript = new();
    private int _navScriptCooldown;
    private const int NavScriptFrameGap = 6;   // ImGui needs a few frames to settle each move
    private const int NavScriptStartFrame = 15;

    // Add-game state. The candidate list is a mutable copy so a browsed folder can be written back
    // onto a candidate before enrollment, exactly as AgentApiServer rewrites its _candidateCache.
    private Task<IReadOnlyList<ScanCandidate>>? _scanTask;
    private readonly List<ScanCandidate> _candidates = new();
    private readonly HashSet<int> _selected = new();
    private Task<(int enrolled, int skipped)>? _enrollTask;
    private string _addStatus = "";

    /// <summary>
    /// Which discovered games the list shows. Not cosmetic on a Deck: the scan now returns installed
    /// Steam titles too, and a library of hundreds of them is unreachable with a thumbstick unless
    /// it can be narrowed. Mirrors the browser UI's filter chips, minus the store axis — Heroic's
    /// storefronts are a Desktop Mode concern and each extra pill costs a d-pad press here.
    /// </summary>
    private enum AddFilter { Suggested, All, Steam, Shortcut, Heroic }
    private AddFilter _addFilter = AddFilter.Suggested;

    private static bool MatchesFilter(ScanCandidate c, AddFilter f) => f switch
    {
        AddFilter.Suggested => !c.HasSteamCloud,
        AddFilter.Steam => c.Source == ScanSource.SteamInstalled,
        AddFilter.Shortcut => c.Source == ScanSource.SteamShortcut,
        AddFilter.Heroic => c.Source == ScanSource.Heroic,
        _ => true,
    };

    private static string FilterLabel(AddFilter f) => f switch
    {
        AddFilter.Suggested => "Suggested",
        AddFilter.Steam => "Steam",
        AddFilter.Shortcut => "Added to Steam",
        AddFilter.Heroic => "Heroic",
        _ => "All",
    };

    /// <summary>
    /// Save-path detection as its own axis, not a source filter — this used to be an
    /// <c>AddFilter.NoPath</c> entry, which meant it competed with Steam/Heroic/etc. instead of
    /// combining with them: there was no way to land on "Heroic games still missing a path". Mirrors
    /// the browser agent UI's "Path:" row (AddGamesView.tsx) for the same reason it exists there.
    /// </summary>
    private enum PathMode { All, Has, Missing }
    private PathMode _pathMode = PathMode.All;

    private static bool MatchesPath(ScanCandidate c, PathMode m) => m switch
    {
        PathMode.Has => !string.IsNullOrEmpty(c.SuggestedSaveDir),
        PathMode.Missing => string.IsNullOrEmpty(c.SuggestedSaveDir),
        _ => true,
    };

    private static string PathModeLabel(PathMode m) => m switch
    {
        PathMode.Has => "Detected",
        PathMode.Missing => "Not detected",
        _ => "All",
    };

    // Set-folder (path browser) state. Listing is cached and only refreshed when the path changes,
    // so navigating does not re-walk the disk every frame.
    private int _folderTargetId = -1;
    private string _browsePath = "";
    private string? _lastListedPath = null;
    private BrowseListing? _listing;
    private string[] _files = Array.Empty<string>();
    private bool _focusFirstEntry;

    private string _copyResult = "";

    private readonly Vector2D<int> _size;

    // Screenshot-and-exit mode: render a few frames so the first-frame layout settles, then capture
    // and quit. Non-interactive, so it works over SSH to a Deck or in a scripted design loop.
    private readonly string? _screenshotPath;
    private int _framesRendered;
    private const int ScreenshotWarmupFrames = 5;
    // Hard ceiling so a scan that never returns cannot hang an unattended capture forever.
    private const int ScreenshotMaxFrames = 600;
    private bool _autoScan;
    private bool _pendingFolderScreen;
    private int _settleFrames;
    // Frames to keep rendering after all work finishes, so a screen change made DURING a frame is
    // actually drawn, and tweens land near their targets, before the framebuffer is read.
    private const int ScreenshotSettleFrames = 20;

    private UiApp(AgentConfig config, Vector2D<int> size, string? screenshotPath, int apiPort)
    {
        _config = config;
        _size = size;
        _screenshotPath = screenshotPath;
        _scanner = new LinuxGameScanner(new Detection(config));
        _browser = new PathBrowser(SteamRoots.BrowseRoots().Concat(HeroicRoots.BrowseRoots()));
        _launch = Daemon.LinuxLaunchCommand();
        _settings = new SettingsScreen(config, new SystemdAutoStart());
        _leaseWarnings = LeaseWarningStore.For(config);
        _activityStore = SyncActivityStore.For(config);
        // "Sync now" reaches the daemon over its own local API — the daemon holds the one SyncEngine
        // that may safely act (Decisions §2), so this is the same LAN-of-one call the browser UI's
        // button already makes, not a duplicated sync path.
        _apiPort = apiPort;
        _localAuth = LocalAuth.LoadOrCreate(config.ConfigPath);
    }

    public static int Run(AgentConfig config, string? sizeOverride = null, string? screenshotPath = null,
        bool gallery = false, string? startScreen = null, bool autoScan = false,
        string? navScript = null, bool navDebug = false, string? pointerAt = null,
        int apiPort = Daemon.DefaultApiPort)
    {
        NavDebug.Enabled = navDebug;
        _pointerPark = ParsePointer(pointerAt);

        // Printed before the window opens, so the probe result is readable even from a run that
        // cannot open a display — which is how a self-contained publish gets checked on a Deck.
        if (navDebug) Console.WriteLine("nav api: " + ImGuiInternal.Status);

        var app = new UiApp(config, ParseSize(sizeOverride), screenshotPath, apiPort);
        if (gallery) app._screen = Screen.Gallery;
        else if (startScreen is not null)
        {
            var target = ParseScreen(startScreen);
            if (target == Screen.SetFolder) { app._screen = Screen.AddGame; app._pendingFolderScreen = true; }
            else app._screen = target;
        }
        app._autoScan = autoScan;
        foreach (var key in ParseNavScript(navScript)) app._navScript.Enqueue(key);
        return app.RunLoop();
    }

    /// <summary>
    /// Which screen to open on. A development affordance: paired with <c>--screenshot</c> it captures
    /// any screen unattended, so a layout change can be reviewed across the whole UI without a person
    /// driving it. Not something a user has any reason to pass.
    /// </summary>
    private static Screen ParseScreen(string name) => name.ToLowerInvariant() switch
    {
        "status" or "overview" => Screen.Status,
        "add" or "addgame" => Screen.AddGame,
        "folder" or "setfolder" => Screen.SetFolder,
        "launch" or "launchsetup" or "steam" => Screen.LaunchSetup,
        "conflicts" or "conflict" => Screen.Conflicts,
        "settings" or "config" => Screen.Settings,
        "gallery" => Screen.Gallery,
        _ => Screen.Status,
    };

    /// <summary>Turn "right,down,a" into the nav keys a gamepad would have produced.</summary>
    private static IEnumerable<ImGuiKey> ParseNavScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)) yield break;
        foreach (var raw in script.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim().ToLowerInvariant();
            var key = token switch
            {
                "up" or "u" => ImGuiKey.GamepadDpadUp,
                "down" or "d" => ImGuiKey.GamepadDpadDown,
                "left" or "l" => ImGuiKey.GamepadDpadLeft,
                "right" or "r" => ImGuiKey.GamepadDpadRight,
                "a" => ImGuiKey.GamepadFaceDown,
                "b" => ImGuiKey.GamepadFaceRight,
                _ => (ImGuiKey?)null,
            };
            if (key is null) Console.Error.WriteLine($"Ignoring unknown --nav step '{token}'.");
            else yield return key.Value;
        }
    }

    /// <summary>
    /// Window size, defaulting to the Deck's native 1280x800. The override exists so the same binary
    /// can be run windowed on a dev box (WSLg) at the exact panel size — testing the layout at any
    /// other size defeats the point. Source: <c>--size WxH</c>, else SAVELOCKER_UI_SIZE.
    /// </summary>
    private static Vector2D<int> ParseSize(string? raw)
    {
        var value = raw ?? Environment.GetEnvironmentVariable("SAVELOCKER_UI_SIZE");
        if (!string.IsNullOrWhiteSpace(value))
        {
            var parts = value.Split('x', 'X');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) &&
                w >= 640 && h >= 400)
                return new Vector2D<int>(w, h);

            Console.Error.WriteLine($"Ignoring bad UI size '{value}' — expected WxH, e.g. 1280x800.");
        }
        return new Vector2D<int>(1280, 800);
    }

    private int RunLoop()
    {
        Window.PrioritizeSdl();

        // Escape hatch for a WSLg dev-preview failure mode: when Weston does not deliver frame
        // callbacks, SDL's vsync swap can block forever inside SwapBuffers, so the window renders
        // every frame to the back buffer but never presents and stays blank regardless of the GL
        // driver (d3d12 OR llvmpipe). Set SAVELOCKER_UI_NO_VSYNC=1 to swap immediately and rule that
        // out. Default stays vsync-on — it is correct on a real Deck's gamescope; FramesPerSecond
        // caps the loop either way, so turning it off does not spin the CPU unbounded.
        var vsync = Environment.GetEnvironmentVariable("SAVELOCKER_UI_NO_VSYNC") is null;

        var options = WindowOptions.Default with
        {
            Title = "SaveLocker",
            Size = _size,
            VSync = vsync,
            // 60 fps flat. The original 30 cap was a battery measure; it was relaxed once the
            // maintainer settled that this is a configuration surface used for minutes at a time,
            // not a game running for hours, with no 3D engine behind it. VSync is what actually
            // stops an unbounded spin, and it stays on.
            FramesPerSecond = 60,
            UpdatesPerSecond = 60,
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.FramebufferResize += size => _gl.Viewport(size);
        _window.Render += OnRender;
        _window.Closing += () =>
        {
            Sound.Shutdown();
            _controller?.Dispose();
            _input?.Dispose();
            _gl?.Dispose();
        };

        _window.Run();
        return 0;
    }

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();

        // Fonts must be added inside onConfigureIO: Silk invokes it before baking the font device
        // texture, so anything added afterwards is silently ignored.
        _controller = new ImGuiController(_gl, _window, _input, null, Theme.LoadFonts);
        Theme.ApplyStyle();
        Art.Load(_gl);

        // A scripted capture must never make noise — on a Deck it would fire into whatever the user
        // is actually listening to. It still opens the device, muted, so the Settings screen reports
        // the real audio state in a screenshot rather than claiming there is no device.
        Sound.Init(_config.UiSoundsMuted || _screenshotPath is not null);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
        io.BackendFlags |= ImGuiBackendFlags.HasGamepad;

        // Silk only tracks gamepad button state once something subscribes to ButtonDown — without
        // this the per-frame Pressed poll reads a stale false and nav is dead. See Gotchas.md.
        foreach (var p in _input.Gamepads) HookPad(p);
        _input.ConnectionChanged += (device, connected) =>
        {
            if (connected && device is IGamepad gp) HookPad(gp);
        };

        // This UI has no text fields, but SDL leaves text input enabled by default and gamescope
        // pops the on-screen keyboard for any client with text input active. Turn it off so the OSK
        // does not cover the UI on launch.
        try { SdlApi.GetApi().StopTextInput(); } catch { /* best-effort */ }
    }

    private void HookPad(IGamepad pad)
    {
        if (!_hookedPads.Add(pad.Index)) return;
        pad.ButtonDown += (_, button) =>
        {
            var key = MapButton(button.Name);
            if (key is null) return;
            lock (_navLock)
            {
                _navQueue.Add(key.Value);
                if (IsDpad(key.Value))
                {
                    var now = Environment.TickCount64;
                    _heldSince[key.Value] = now;
                    _lastRepeat[key.Value] = now;
                }
            }
        };
        pad.ButtonUp += (_, button) =>
        {
            var key = MapButton(button.Name);
            if (key is null) return;
            lock (_navLock) { _heldSince.Remove(key.Value); _lastRepeat.Remove(key.Value); }
        };
    }

    private static bool IsDpad(ImGuiKey k) =>
        k is ImGuiKey.GamepadDpadUp or ImGuiKey.GamepadDpadDown
          or ImGuiKey.GamepadDpadLeft or ImGuiKey.GamepadDpadRight;

    private static ImGuiKey? MapButton(ButtonName name) => name switch
    {
        ButtonName.DPadUp => ImGuiKey.GamepadDpadUp,
        ButtonName.DPadDown => ImGuiKey.GamepadDpadDown,
        ButtonName.DPadLeft => ImGuiKey.GamepadDpadLeft,
        ButtonName.DPadRight => ImGuiKey.GamepadDpadRight,
        ButtonName.A => ImGuiKey.GamepadFaceDown,   // activate
        ButtonName.B => ImGuiKey.GamepadFaceRight,  // cancel
        _ => null,
    };

    private void OnRender(double delta)
    {
        // Feed one scripted press every few frames, through the same queue the pad writes to.
        // Nothing is injected until focus has settled: SetKeyboardFocusHere resolves at the end of
        // the first frame and overrides nav movement, so a press on frame 0 is simply eaten.
        // Wait for in-flight work before replaying: a real user presses Right after a scan has
        // finished, not into a spinner, and a script that races the scan tests nothing useful.
        var scriptReady = _framesRendered >= NavScriptStartFrame
                          && _scanTask is not { IsCompleted: false }
                          && _enrollTask is not { IsCompleted: false };
        if (_navScript.Count > 0 && scriptReady)
        {
            if (--_navScriptCooldown <= 0)
            {
                _navScriptCooldown = NavScriptFrameGap;
                lock (_navLock) _navQueue.Add(_navScript.Dequeue());
            }
        }

        NavDebug.BeginFrame();
        Widgets.BeginFrame();
        FeedImGuiNav();
        // Rail badge and the status screen's per-game prompt both need this regardless of which
        // screen is active, so it is polled here rather than inside DrawConflicts.
        PollConflictState();
        // Before Update, which is what calls NewFrame: a queued mouse position must be in the queue
        // NewFrame drains, because NewFrame is also where HoveredWindow is resolved. MouseDelta read
        // here is last frame's, which costs the highlight a frame nobody can perceive.
        TrackCursorOwner();

        // SDL's error string is sticky — it is never cleared on success, only overwritten on the
        // next failure. Silk's SdlContext.FramebufferSize calls SDL_GL_GetDrawableSize (which
        // returns void) and then decides whether it failed by reading SDL_GetError, so ANY stale
        // error left behind by an earlier harmless probe surfaces here as a fatal
        // "That operation is not supported" on the first frame. Clearing it each frame is what SDL
        // documents callers should do before a call whose result they test this way. Costs nothing.
        try { SdlApi.GetApi().ClearError(); } catch { /* best-effort */ }

        _controller.Update((float)delta);


        _gl.ClearColor(Theme.BgGlobal.X, Theme.BgGlobal.Y, Theme.BgGlobal.Z, 1.0f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
                    ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollbar;

        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("SaveLocker", flags);
        ImGui.PopStyleVar();

        var size = ImGui.GetIO().DisplaySize;

        if (_screen == Screen.Gallery)
        {
            // The dev gallery takes the whole surface: it is taller than the shell's content area
            // and exists to show components, not to be navigated like a real screen.
            ImGui.SetCursorPos(new Vector2(Theme.Layout.Gutter, Theme.Layout.Gutter));
            ImGui.BeginChild("gallery",
                new Vector2(size.X - Theme.Layout.Gutter * 2, size.Y - Theme.Layout.Gutter * 2));
            Gallery.Draw();
            ImGui.EndChild();
        }
        else
        {
            DrawHeader(size);
            DrawRail(size);
            DrawContent(size);
            DrawHintBar(size);
            DrawSeparators(size);
        }

        // Drawn before the request is aged and before the crossing is resolved, so the overlay shows
        // the state that actually governed THIS frame's draw rather than next frame's.
        NavDebug.Draw(new NavDebug.Frame(
            _screen.ToString(), _focusZone.ToString(), _activeRailId, _bestContentId,
            _pendingCrossFrames));

        // Anything left armed had no enabled widget to land on this frame; let it age out.
        Widgets.AgeFocusRequest();

        RecoverStrandedCursor();
        ResolvePaneCrossing();

        ImGui.End();
        _controller.Render();

        // A mid-transition capture would be a dim, offset frame — the fade counts as work in
        // progress for screenshot purposes.
        // _conflictsTask/_versionTasks/_versionStatsTasks are deliberately NOT here: they are an
        // ambient background poll, not a user-triggered write, and gating settle on them would make
        // every screenshot anywhere in the app wait on a network round trip. _resolveTask IS a
        // user-triggered write (same category as _syncNowTask) — without it, a scripted --nav that
        // presses a Keep button gets the process torn down by Environment.Exit below before the
        // resolve request the button just started ever reaches the server.
        var busy = _scanTask is { IsCompleted: false } || _enrollTask is { IsCompleted: false }
                   || _syncNowTask is { IsCompleted: false } || _resolveTask is { IsCompleted: false }
                   || _pendingFolderScreen || _screenFade < 1f || _navScript.Count > 0;
        _settleFrames = busy ? 0 : _settleFrames + 1;

        if (_screenshotPath is not null && ++_framesRendered >= ScreenshotWarmupFrames
            && (_settleFrames >= ScreenshotSettleFrames || _framesRendered >= ScreenshotMaxFrames))
        {
            int code = 0;
            try
            {
                var fb = _window.FramebufferSize;
                Screenshot.Capture(_gl, fb.X, fb.Y, _screenshotPath);
                Console.WriteLine($"Wrote {fb.X}x{fb.Y} screenshot to {_screenshotPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Screenshot failed: " + ex.Message);
                code = 1;
            }

            // Exit rather than Close(): we are inside the render callback, and Silk calls
            // SwapBuffers after it returns. Close() runs the Closing handler, which disposes the GL
            // context, so that SwapBuffers then throws "Context not created". The PNG stream is
            // already flushed and closed by this point, and this mode's entire job is done.
            Console.Out.Flush();
            Environment.Exit(code);
        }
    }

    /// <summary>
    /// Move the cursor between the rail and the content area.
    ///
    /// ImGui scores directional nav purely on geometry, and the two panes routinely share no
    /// vertical overlap — a rail entry near the top has nothing beside it when the content's first
    /// control sits far lower — so it simply refuses the move. That is what made the UI feel like
    /// it needed A to "enter" a pane and B to leave it.
    ///
    /// So both crossings are explicit and unconditional: the rail is one column, so Right out of it
    /// can never mean anything else, and Left out of the content always returns to the rail entry for
    /// the screen you are on. Each fires a focus request that <see cref="Widgets"/> serves through
    /// igSetFocusID on the very next frame.
    ///
    /// Left is NOT deferred to see whether ImGui would move the cursor itself first. It was, so that
    /// Left could step through a screen's own columns before leaving the pane — but no screen has
    /// side-by-side focusable columns (the two-column layouts are text against controls), and the
    /// deferral cost a frame on every press and left the request armed when it did not land.
    /// </summary>
    private void ResolvePaneCrossing()
    {
        // A cross that had nowhere to land completes as soon as the content has a control.
        if (_pendingCrossFrames > 0)
        {
            _pendingCrossFrames--;
            if (_bestContentId != 0)
            {
                _pendingCrossFrames = 0;
                EnterContent();
            }
        }

        if (_navRightFired && _focusZone == Zone.Rail)
        {
            if (_bestContentId != 0) EnterContent();
            else _pendingCrossFrames = PendingCrossFrames;   // scan still running
        }
        else if (_navLeftFired && _focusZone == Zone.Content)
        {
            // Back to the rail entry for the screen you are on — never the nearest one by pixels.
            _focusZone = Zone.Rail;
            Widgets.RequestFocus(_activeRailId);
        }

        // B has to be handled here now. It used to be ImGui's built-in NavCancel and nothing else,
        // which worked only by accident: with the cursor app-placed, RecoverStrandedCursor puts back
        // whatever NavCancel cleared, in the same frame. Owning it also fixes a hint bar that has
        // been lying — on Set save folder the bar promises "Cancel", but NavCancel only ever moved the
        // nav cursor and left the screen up; the on-screen Cancel button was the only way out.
        if (_navBackFired)
        {
            // In the folder browser B means "up a directory", not "cancel". Inside a file tree that is
            // what back reads as, and cancelling is already reachable two other ways (the Cancel
            // button, or Left out to the rail) — so spending B on a third exit wasted it.
            // At the roots there is nothing above to climb to, so back leaves the screen rather than
            // becoming a dead button.
            if (_screen == Screen.SetFolder)
            {
                bool canClimb = _listing is not null &&
                                (_listing.Parent is not null || !string.IsNullOrEmpty(_listing.Path));
                if (canClimb)
                {
                    _browsePath = _listing!.Parent ?? "";
                }
                else
                {
                    _screen = Screen.AddGame;
                    _bestContentId = 0;
                    _focusZone = Zone.Rail;
                    Widgets.RequestFocus(_activeRailId);
                }
            }
            else if (_focusZone == Zone.Content)
            {
                _focusZone = Zone.Rail;
                Widgets.RequestFocus(_activeRailId);
            }
        }

        _navLeftFired = _navRightFired = _navBackFired = false;
    }

    private void EnterContent()
    {
        _focusZone = Zone.Content;
        Widgets.RequestFocus(_bestContentId);
    }

    /// <summary>
    /// Put the cursor back on a real control if no submitted item claimed it this frame.
    ///
    /// A Deck has no pointer, so the focus ring IS the cursor: if ImGui leaves NavId on something no
    /// widget answers for, the screen shows a highlight with nothing selected inside it and the D-pad
    /// reads as dead. That is what the hint bar appearing "selectable, with no options in it"
    /// actually was — a pane-sized highlight whose bottom edge sits on the hint bar, reported from
    /// the Deck as a footer you could navigate to.
    ///
    /// This is an invariant rather than a patch for one cause: whatever strands the cursor — an item
    /// that stopped being submitted, a list that emptied, a screen whose ids all changed — the cursor
    /// belongs on a control, and there is now a primitive that can put it there.
    /// </summary>
    private void RecoverStrandedCursor()
    {
        if (Widgets.CursorOnRealItem || Widgets.FocusRequestPending)
        {
            _strandedFrames = 0;
            return;
        }

        _strandedFrames++;

        // Prefer where the cursor last legitimately was — but only once. If that item is gone rather
        // than merely unfocused (browsing into another folder retires every row id, so this is a real
        // path, not a theoretical one) then re-requesting it can never land, and retrying it forever
        // would leave the cursor stranded while looking busy. Second attempt goes to the pane anchor,
        // which is always a live id.
        var target = _strandedFrames == 1 && Widgets.LastGoodId != 0
            ? Widgets.LastGoodId
            : _focusZone == Zone.Rail ? _activeRailId : _bestContentId;

        Widgets.RequestFocus(target);
    }

    /// <summary>
    /// Whether both panes must stay navigable so a hand-off can complete.
    ///
    /// Only the fallback needs this. <c>SetKeyboardFocusHere</c> can only move a cursor that is not
    /// already inside a <c>NoNav</c> window, so suppressing the pane it is leaving strands it. That
    /// concession is also the bug: while both panes are live, ImGui's geometric scoring can carry a
    /// Down out of the content and onto "Quit". <c>igSetFocusID</c> needs no concession — it writes
    /// the cursor's new home directly — so with the internal API each pane is navigable only while it
    /// owns the cursor, which is what makes Up/Down unable to cross panes at all.
    /// </summary>
    private static bool BothPanesForHandoff =>
        !ImGuiInternal.Available && Widgets.FocusRequestPending;

    private bool Connected => !string.IsNullOrEmpty(_config.ApiKey);

    /// <summary>
    /// The top bar: mark, connection state, server. Mirrors the console's StatusHeader so the two
    /// surfaces are recognisably the same product.
    /// </summary>
    private void DrawHeader(Vector2 size)
    {
        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.BgCard);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(Theme.Layout.Gutter, Theme.Space.Sm));
        ImGui.BeginChild("header", new Vector2(size.X, Theme.Layout.HeaderHeight),
            ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoNav);

        // Vertical centring is measured inside the child's content box, so the padding pushed above
        // is already accounted for — do not subtract it again.
        const float markH = 40f;
        if (Art.Logo.Ok)
        {
            var inner = Theme.Layout.HeaderHeight - Theme.Space.Sm * 2;
            ImGui.SetCursorPosY(Theme.Space.Sm + MathF.Max(0f, (inner - markH) / 2f));
            ImGui.Image(Art.Logo.Id, new Vector2(Art.Logo.WidthAt(markH), markH));
            ImGui.SameLine(0, Theme.Space.Md);
        }

        ImGui.BeginGroup();
        Widgets.EyebrowLabel("Agent Status");
        var accent = Connected ? Theme.AccentGreen : Theme.AccentAmber;
        Widgets.StatusDot(accent, 8f);
        ImGui.SameLine(0, Theme.Space.Sm);
        Widgets.Text(Connected ? "CONNECTED" : "NOT ENROLLED", accent, Theme.BodyStrong);
        ImGui.EndGroup();

        // Server chip, right-aligned.
        var url = _config.ServerUrl?.Replace("https://", "").Replace("http://", "") ?? "";
        if (!string.IsNullOrEmpty(url))
        {
            Theme.PushFont(Theme.Mono);
            var chipW = ImGui.CalcTextSize(url).X + 52f;
            Theme.PopFont(Theme.Mono);

            ImGui.SameLine();
            var pad = ImGui.GetContentRegionAvail().X - chipW;
            if (pad > 0) { ImGui.Dummy(new Vector2(pad, 0)); ImGui.SameLine(); }
            ImGui.SetCursorPosY((Theme.Layout.HeaderHeight - 26f) / 2f - Theme.Space.Sm);
            Widgets.Badge(url, Theme.AccentGreen, Icons.Server, mono: true);
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>The left rail of top-level destinations. Set save folder is a sub-flow of Add game,
    /// so it is deliberately not a rail entry.</summary>
    private void DrawRail(Vector2 size)
    {
        var height = size.Y - Theme.Layout.HeaderHeight - Theme.Layout.HintBarHeight;

        ImGui.SetCursorPos(new Vector2(0, Theme.Layout.HeaderHeight));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.BgCard);
        // Horizontal padding is FocusClearance, not Space.Sm: rail entries are full-width, so the
        // padding is the only room their focus ring has.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(Theme.Layout.FocusClearance, Theme.Space.Md));
        // Flattened so the rail is never a nav *container* — that is what drew a highlight around
        // the whole pane and produced the A-to-enter / B-to-leave feel. Navigable only while the
        // cursor lives here: without that gate both panes share one nav space and a Down inside the
        // content lands on a rail entry, which is why Down from "Scan for games" jumped to
        // "Add game" and left the discovered list unreachable.
        var railFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NavFlattened;
        if (_focusZone != Zone.Rail && !BothPanesForHandoff)
            railFlags |= ImGuiWindowFlags.NoNav;
        ImGui.BeginChild("rail", new Vector2(Theme.Layout.RailWidth, height),
            ImGuiChildFlags.AlwaysUseWindowPadding, railFlags);
        NavDebug.PushScope("rail");

        // Order matches agent-ui's Sidebar.tsx (Overview, Add Games, Conflicts, ...) so the two
        // surfaces read as the same product.
        var conflictsLabel = _openConflicts.Count > 0 ? $"Conflicts ({_openConflicts.Count})" : "Conflicts";
        var items = new (string Label, Icons.Glyph Icon, Screen Target, bool Active)[]
        {
            ("Overview", Icons.Monitor, Screen.Status, _screen == Screen.Status),
            ("Add game", Icons.Plus, Screen.AddGame, _screen is Screen.AddGame or Screen.SetFolder),
            (conflictsLabel, Icons.GitBranch, Screen.Conflicts, _screen == Screen.Conflicts),
            ("Steam setup", Icons.HardDrive, Screen.LaunchSetup, _screen == Screen.LaunchSetup),
            ("Settings", Icons.Settings, Screen.Settings, _screen == Screen.Settings),
        };

        foreach (var (label, icon, target, active) in items)
        {
            // Land the cursor on the rail entry for the screen already being shown. Without an
            // initial nav target ImGui starts with nothing focused, so the first D-pad press only
            // picks a starting item and appears to do nothing.
            //
            // ⚠️ This is suspected of not working and is kept only because that is unproven. The rail
            // is a NavFlattened child and SetKeyboardFocusHere is a tabbing request confined to the
            // active focus scope (ocornut/imgui#7226) — the restriction ImGuiInternal exists to get
            // around — and it resolves at the end of frame 0, on top of the request
            // RecoverStrandedCursor places the same frame. If "A does nothing at open" survives the
            // hover fix on hardware, delete this block first: RecoverStrandedCursor already seeds
            // frame 0 through igSetFocusID, which is why removing it changed nothing under WSLg.
            if (_focusRailOnce && active)
            {
                _focusRailOnce = false;
                ImGui.SetKeyboardFocusHere();
            }
            if (Widgets.RailItem(label, icon, active, id: target == Screen.Conflicts ? "Conflicts" : label))
                Go(target);
            if (active) _activeRailId = Widgets.LastRailItemId;
        }

        // Quit sits at the bottom, away from the destinations — it is not a place you go.
        var used = ImGui.GetCursorPosY();
        var spare = height - used - (ImGui.GetTextLineHeight() + Theme.Space.Md * 2) - Theme.Space.Md;
        if (spare > 0) ImGui.Dummy(new Vector2(0, spare));

        if (Widgets.RailItem("Quit", Icons.X, false)) _window.Close();

        NavDebug.PopScope();
        ImGui.EndChild();
        NavDebug.NoteContainer("rail");
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// The three shell separators, drawn on the FOREGROUND list after every pane is submitted.
    ///
    /// They used to be drawn on the root window's draw list as each pane was built, which put them
    /// UNDER the panes: a child window's draw list merges after its parent's, and the rail and content
    /// backgrounds both start at exactly y = HeaderHeight, so they painted over the header rule. It
    /// survived only in the 1 px gutter at x = RailWidth — the "remnants at the tops of the vertical
    /// lines" reported from the Deck. Drawing them last is what makes them continuous.
    /// </summary>
    private static void DrawSeparators(Vector2 size)
    {
        var dl = ImGui.GetForegroundDrawList();
        var colour = ImGui.ColorConvertFloat4ToU32(Theme.Border);
        var railBottom = size.Y - Theme.Layout.HintBarHeight;

        dl.AddLine(new Vector2(0, Theme.Layout.HeaderHeight),
                   new Vector2(size.X, Theme.Layout.HeaderHeight), colour, 1f);
        dl.AddLine(new Vector2(Theme.Layout.RailWidth, Theme.Layout.HeaderHeight),
                   new Vector2(Theme.Layout.RailWidth, railBottom), colour, 1f);
        dl.AddLine(new Vector2(0, railBottom), new Vector2(size.X, railBottom), colour, 1f);
    }

    private void DrawContent(Vector2 size)
    {
        var height = size.Y - Theme.Layout.HeaderHeight - Theme.Layout.HintBarHeight;

        ImGui.SetCursorPos(new Vector2(Theme.Layout.RailWidth + 1f, Theme.Layout.HeaderHeight));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.BgGlobal);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(Theme.Layout.Gutter, Theme.Layout.Gutter));
        var contentFlags = ImGuiWindowFlags.NavFlattened;
        if (_focusZone != Zone.Content && !BothPanesForHandoff)
            contentFlags |= ImGuiWindowFlags.NoNav;
        ImGui.BeginChild("content",
            new Vector2(size.X - Theme.Layout.RailWidth - 1f, height),
            ImGuiChildFlags.AlwaysUseWindowPadding, contentFlags);
        NavDebug.PushScope("content");

        // Cross-fade on navigation. Without it a rail press swaps the entire right-hand two-thirds
        // of the screen between two frames, which reads as a glitch rather than a transition.
        // Ramp is time-based, not per-frame, so it is identical whatever the loop is doing.
        if (_renderedScreen != _screen)
        {
            _renderedScreen = _screen;
            _screenFade = 0f;
        }
        _screenFade = MathF.Min(1f, _screenFade + ImGui.GetIO().DeltaTime / ScreenFadeSeconds);

        // Ease-out cubic: fast to mostly-visible, then settles. A linear fade reads as sluggish
        // because the eye spends most of it watching the dim half.
        var eased = 1f - MathF.Pow(1f - _screenFade, 3f);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, eased);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (1f - eased) * 12f);

        // Collect this screen's focusable controls so Right can land on the visually topmost one
        // rather than whichever happened to be drawn first.
        Widgets.BeginFocusScan();

        switch (_screen)
        {
            case Screen.Status: DrawStatus(); break;
            case Screen.AddGame: DrawAddGame(); break;
            case Screen.SetFolder: DrawSetFolder(); break;
            case Screen.LaunchSetup: DrawLaunchSetup(); break;
            case Screen.Conflicts: DrawConflicts(); break;
            case Screen.Settings: _settings.Draw(); break;
        }

        var best = Widgets.EndFocusScan();
        if (best != 0) _bestContentId = best;

        ImGui.PopStyleVar();
        NavDebug.PopScope();
        ImGui.EndChild();
        NavDebug.NoteContainer("content");
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// The gamepad hint bar. The React UI has no equivalent because a desktop has a pointer; on a
    /// Deck this is the only thing telling the user what the face buttons do.
    /// </summary>
    private void DrawHintBar(Vector2 size)
    {
        var y = size.Y - Theme.Layout.HintBarHeight;

        ImGui.SetCursorPos(new Vector2(0, y));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.BgCard);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(Theme.Layout.Gutter, Theme.Space.Sm + 2f));
        ImGui.BeginChild("hints", new Vector2(size.X, Theme.Layout.HintBarHeight),
            ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoNav);

        Widgets.GamepadHint("A", "Select");
        ImGui.SameLine(0, Theme.Space.Lg);
        Widgets.GamepadHint("B", _screen == Screen.SetFolder ? "Cancel" : "Back");
        ImGui.SameLine(0, Theme.Space.Lg);
        Widgets.GamepadHintIcon(Icons.Dpad, "Move");

        var version = $"SaveLocker {BuildVersion}";
        Theme.PushFont(Theme.Caption);
        var vw = ImGui.CalcTextSize(version).X;
        Theme.PopFont(Theme.Caption);

        ImGui.SameLine();
        var pad = ImGui.GetContentRegionAvail().X - vw;
        if (pad > 0) { ImGui.Dummy(new Vector2(pad, 0)); ImGui.SameLine(); }
        ImGui.AlignTextToFramePadding();
        Widgets.Text(version, Theme.TextDim, Theme.Caption);

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private static string BuildVersion =>
        System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                System.Reflection.Assembly.GetExecutingAssembly())
            ?.InformationalVersion.Split('+')[0] ?? "dev";

    private void Go(Screen target)
    {
        if (_screen == target) return;
        _screen = target;
        _copyResult = "";
        // The new screen's content has not been measured yet, and the cursor stays on the rail
        // entry the user just activated.
        _focusZone = Zone.Rail;
        _bestContentId = 0;
        _pendingCrossFrames = 0;
    }

    private void DrawStatus()
    {
        DrawLeaseWarnings();

        if (!Connected)
        {
            Widgets.Banner("notenrolled", "This device is not enrolled",
                "Run  savelocker enroll --file <policy.json>  once in Desktop Mode, then come back here.",
                Theme.AccentAmber, Icons.AlertTriangle);
            Widgets.Gap(Theme.Space.Md);
        }

        // Stat tiles: the console's headline element, and what fills the top of the panel.
        var avail = ImGui.GetContentRegionAvail().X;
        var tileW = (avail - Theme.Space.Md * 2) / 3f;
        var lastSync = _config.LastSyncTime.HasValue
            ? FormatAgo(DateTime.UtcNow - _config.LastSyncTime.Value)
            : "-";

        Widgets.StatTile(_config.Games.Count.ToString(), "Games Tracked", Theme.AccentGreen, tileW, 112f);
        ImGui.SameLine(0, Theme.Space.Md);
        Widgets.StatTile(_config.TotalSavesPushed.ToString(), "Saves Backed Up", Theme.TextPrimary, tileW, 112f);
        ImGui.SameLine(0, Theme.Space.Md);
        Widgets.StatTile(lastSync, "Last Sync", Theme.TextMuted, tileW, 112f);

        Widgets.Gap(Theme.Space.Lg);

        // What is happening right now, mirroring the browser agent UI's Activity card — including
        // its "Sync now" button. This screen still never SYNCS: the daemon, a separate process, is
        // the one that holds the sync engine and its lease state, so the button POSTs to the
        // daemon's own /api/sync and everything shown here is read back out of SyncActivityStore.
        // What this screen must not become is a second place that builds an engine of its own.
        DrawActivity();
        Widgets.Gap(Theme.Space.Lg);

        Widgets.TwoColumn("status", 0.58f,
            left: () =>
            {
                Widgets.SectionHeader("Tracked games");
                if (_config.Games.Count == 0)
                {
                    Widgets.Text("No games tracked yet.", Theme.TextMuted);
                    Widgets.Gap(Theme.Space.Md);
                    if (Widgets.PillButton("Add a game", Widgets.ButtonKind.Primary, Icons.Plus))
                        Go(Screen.AddGame);
                }
                else
                {
                    foreach (var g in _config.Games)
                    {
                        var missing = string.IsNullOrEmpty(g.SaveDirectory);
                        // A conflict outranks the missing-folder prompt: it is a decision pending
                        // right now, not a one-time setup step (same priority order as the Decky
                        // chip states table in tasks/conflict-resolution-ui/reference/03-platform-
                        // ux-flows.md). Reachable here without ever attempting a launch first.
                        var conflicted = _openConflicts.Any(c => c.GameId == g.GameId);
                        // Conflict outranks the message/icon, but a game can be both conflicted AND
                        // missing a folder (e.g. untracked then re-adopted while its conflict stayed
                        // open) — the trailing badge names both so the folder problem stays visible
                        // instead of disappearing behind the conflict.
                        var pressed = Widgets.ListRow($"g{g.Name}", g.Name,
                            conflicted ? "Save conflict - pick which one to keep"
                                : missing ? "No save folder set" : g.SaveDirectory,
                            conflicted || missing ? Icons.AlertTriangle : Icons.Folder,
                            trailing: conflicted && missing ? "conflict + needs setup"
                                : conflicted ? "conflict" : missing ? "needs setup" : null,
                            trailingColour: (conflicted || missing) ? Theme.AccentAmber : null,
                            chevron: conflicted);
                        if (pressed && conflicted) Go(Screen.Conflicts);
                    }
                    Widgets.Gap(Theme.Space.Md);
                    if (Widgets.PillButton("Add a game", Widgets.ButtonKind.Primary, Icons.Plus))
                        Go(Screen.AddGame);
                }
            },
            right: () =>
            {
                Widgets.SectionHeader("This device");
                InfoRow("Machine", _config.MachineName ?? "-");
                InfoRow("Server", _config.ServerUrl ?? "-", mono: true);
                InfoRow("Agent", BuildVersion, mono: true);
                InfoRow("Status", Connected ? "Enrolled" : "Not enrolled",
                    colour: Connected ? Theme.AccentGreen : Theme.AccentAmber);

                Widgets.Gap(Theme.Space.Lg);
                Widgets.SectionHeader("Next step");

                // A staged update outranks the launch-option reminder: it is the only thing on this
                // screen that says the agent is about to change, and Game Mode has nowhere else to
                // say it.
                //
                // There is still no button, deliberately — this screen cannot restart the unit it is
                // being read from. What it CAN do is stop being a dead end. The old wording said the
                // update lands "the next time this device starts SaveLocker" and then "Nothing to
                // do", which is true, opaque and reads as "you cannot do anything": nothing on
                // screen says that phrase means a systemd --user unit, so someone who wants it now
                // has to guess. Updater.ApplyInstruction() answers it for this device instead.
                if (Updater.PendingVersion(_config) is { } pending)
                {
                    Widgets.TextWrapped(
                        $"SaveLocker {pending} is downloaded and ready. "
                        + Updater.ApplyInstruction(),
                        Theme.AccentGreen);
                    Widgets.Gap(Theme.Space.Sm);
                }

                Widgets.TextWrapped(
                    "Each game needs the SaveLocker launch option set once in Steam before it syncs.",
                    Theme.TextMuted);
                Widgets.Gap(Theme.Space.Sm);
                if (Widgets.PillButton("Steam setup", Widgets.ButtonKind.Secondary, Icons.Settings))
                    Go(Screen.LaunchSetup);
            });
    }

    /// <summary>
    /// What is syncing right now, read from the daemon's <see cref="SyncActivityStore"/> — a byte
    /// progress bar for a push (the direction slow enough to want one) and a short rolling log of
    /// what just happened, the same feed the browser agent UI's Activity card shows.
    /// </summary>
    private void DrawActivity()
    {
        var now = Environment.TickCount64;
        if (now - _activityReadAt > ActivityPollMs || _activityReadAt == 0)
        {
            (_activityCurrent, _activityRecent) = _activityStore.Read();
            _activityReadAt = now;
        }

        if (_syncNowTask is { IsCompleted: true })
        {
            _syncNowMessage = _syncNowTask.IsCompletedSuccessfully
                ? _syncNowTask.Result
                : "Sync failed: " + (_syncNowTask.Exception?.GetBaseException().Message ?? "unknown error");
            _syncNowTask = null;
        }

        Widgets.BeginCard("activity", new Vector2(0, 0));
        Widgets.SectionHeader("Activity");

        bool syncing = _syncNowTask is { IsCompleted: false };
        if (Widgets.PillButton(syncing ? "Syncing..." : "Sync now", Widgets.ButtonKind.Secondary,
                enabled: !syncing && Connected))
        {
            _syncNowMessage = null;
            _syncNowTask = SyncNowAsync();
        }
        if (!string.IsNullOrEmpty(_syncNowMessage))
        {
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.AlignTextToFramePadding();
            Widgets.Text(_syncNowMessage, Theme.TextMuted, Theme.Caption);
        }
        Widgets.Gap(Theme.Space.Sm);

        var current = _activityCurrent;
        var active = current is { Phase: not SyncPhase.Idle };

        if (!active)
        {
            Widgets.StatusDot(Theme.TextDim, 7f);
            ImGui.SameLine(0, Theme.Space.Sm);
            ImGui.AlignTextToFramePadding();
            // A plain hyphen, not an em dash: the embedded font's glyph atlas is ASCII + Latin-1
            // only (Theme.LoadFonts), so U+2014 renders as a missing-glyph box on this screen —
            // every other real (non-comment) string in this file already avoids it for that reason.
            Widgets.Text("Idle - nothing syncing right now.", Theme.TextMuted, Theme.Caption);
        }
        else
        {
            var verb = current!.Phase switch
            {
                SyncPhase.Pushing => "Pushing",
                SyncPhase.Pulling => "Pulling",
                _ => "Settling",
            };
            Widgets.StatusDot(Theme.AccentGreen, 7f);
            ImGui.SameLine(0, Theme.Space.Sm);
            ImGui.AlignTextToFramePadding();
            Widgets.Text($"{verb} {current.GameName}...", Theme.TextPrimary, Theme.Caption);

            if (current.Phase == SyncPhase.Pushing && current.BytesTotal > 0)
            {
                Widgets.Gap(Theme.Space.Xs);
                var pct = (float)current.BytesDone / current.BytesTotal;
                Widgets.ProgressBar(pct, ImGui.GetContentRegionAvail().X);
                Widgets.Gap(Theme.Space.Xs);
                Widgets.Text($"{FormatBytes(current.BytesDone)} / {FormatBytes(current.BytesTotal)}",
                    Theme.TextDim, Theme.Caption);
            }
        }

        // Room for a handful of lines only — this is a status card on an Overview screen, not the
        // log itself, so it shows just enough to answer "did the last thing work" at a glance.
        if (_activityRecent.Count > 0)
        {
            Widgets.Gap(Theme.Space.Sm);
            foreach (var e in _activityRecent.Take(3))
                Widgets.Text($"{e.TimestampUtc.ToLocalTime():HH:mm:ss}  {e.Message}", Theme.TextDim, Theme.Caption);
        }

        Widgets.EndCard();
    }

    /// <summary>
    /// POSTs to the daemon's own <c>/api/sync</c> — the exact route and token scheme
    /// <see cref="AgentApiServer"/> already serves the browser UI's "Sync now" button, on
    /// <c>localhost</c> only and gated on <see cref="LocalAuth"/>. This process never builds a
    /// second <see cref="SyncEngine"/>: the daemon is the one long-lived holder of lease state, so
    /// asking it to sync is the only safe way for a second process to make one happen at all.
    /// <para>
    /// The timeout is explicit because <c>/api/sync</c> holds its request open for the WHOLE
    /// multi-game sync, and HttpClient's own default is 100s — the same fixed ceiling the chunked
    /// upload protocol exists to get out from under. A large save on a slow uplink took longer than
    /// that and this screen reported "Sync failed" over a push that was still running and about to
    /// succeed, which is the one thing a status surface must never do.
    /// </para>
    /// </summary>
    private async Task<string> SyncNowAsync()
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_apiPort}/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.Add(LocalAuth.HeaderName, _localAuth.Token);
        using var response = await http.PostAsync("api/sync", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SyncNowResponse>();
        return body?.Message ?? "Done.";
    }

    private static string FormatBytes(long n)
    {
        if (n < 1024) return $"{n} B";
        if (n < 1024 * 1024) return $"{n / 1024.0:0.0} KB";
        return $"{n / (1024.0 * 1024.0):0.0} MB";
    }

    /// <summary>
    /// Conflict-risk banners, read from the shared store. Dismissal writes through so the same
    /// warning does not reappear in the browser UI afterwards.
    /// </summary>
    private void DrawLeaseWarnings()
    {
        var now = Environment.TickCount64;
        if (now - _warningsReadAt > WarningsPollMs || _warningsReadAt == 0)
        {
            _warnings = _leaseWarnings.Read();
            _warningsReadAt = now;
        }

        if (_warnings.Count == 0) return;

        foreach (var w in _warnings)
        {
            if (Widgets.Banner($"lease{w.GameName}", $"Save conflict risk - {w.GameName}",
                    $"{w.HolderMachine} already has this game checked out. You launched without "
                    + "pulling their latest save, so a conflict will likely appear in the console "
                    + "when you exit.",
                    Theme.AccentAmber, Icons.AlertTriangle, dismissible: true))
            {
                _leaseWarnings.Clear(w.GameName);
                _warningsReadAt = 0;   // force a re-read next frame
            }
            Widgets.Gap(Theme.Space.Sm);
        }
        Widgets.Gap(Theme.Space.Sm);
    }

    /// <summary>A label/value pair, aligned into a column so a stack of them reads as a table.</summary>
    private static void InfoRow(string label, string value, bool mono = false, Vector4? colour = null)
    {
        Widgets.Text(label, Theme.TextMuted, Theme.Caption);
        ImGui.SameLine(96f);
        Widgets.Text(value, colour ?? Theme.TextPrimary, mono ? Theme.Mono : Theme.Body);
    }

    private void DrawAddGame()
    {
        if (!Connected)
        {
            Widgets.Banner("addblocked", "This device is not enrolled",
                "Games cannot be added yet. Run  savelocker enroll --file <policy.json>  once in "
                + "Desktop Mode, then return here.",
                Theme.AccentAmber, Icons.AlertTriangle);
            return;
        }

        // Dev affordance: start the scan without a button press, so an unattended capture can show
        // the populated candidate list rather than the empty state.
        if (_autoScan && _scanTask is null && _candidates.Count == 0)
        {
            _autoScan = false;
            _scanTask = _scanner.ScanAsync();
        }

        bool scanning = _scanTask is { IsCompleted: false };
        if (scanning)
        {
            var dl = ImGui.GetWindowDrawList();
            Icons.Spinner(dl, ImGui.GetCursorScreenPos(), 22f, Theme.AccentGreen, 2.5f);
            ImGui.Dummy(new Vector2(22, 22));
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.AlignTextToFramePadding();
            Widgets.Text("Scanning for games...", Theme.TextMuted);
        }
        else if (Widgets.PillButton("Scan for games", Widgets.ButtonKind.Primary, Icons.Search))
        {
            _scanTask = _scanner.ScanAsync();
        }

        // Fold a finished scan into the mutable candidate list.
        if (_scanTask is { IsCompletedSuccessfully: true } done)
        {
            _candidates.Clear();
            _candidates.AddRange(done.Result);
            _selected.Clear();
            _addStatus = $"Found {_candidates.Count} game(s).";
            _scanTask = null;

            // Dev affordance: `--screen folder` has nothing to browse until a scan has produced a
            // candidate, so entering that screen is deferred until one exists.
            if (_pendingFolderScreen && _candidates.Count > 0)
            {
                _pendingFolderScreen = false;
                EnterSetFolder(0);
            }
        }
        else if (_scanTask is { IsFaulted: true } faulted)
        {
            _addStatus = "Scan failed: " + (faulted.Exception?.GetBaseException().Message ?? "unknown error");
            _scanTask = null;
        }

        // Absorb a finished enroll.
        if (_enrollTask is { IsCompleted: true })
        {
            if (_enrollTask.IsCompletedSuccessfully)
            {
                var (enrolled, skipped) = _enrollTask.Result;
                _addStatus = $"Enrolled {enrolled}, skipped {skipped}.";
                _selected.Clear();
                _candidates.Clear();
                _screen = Screen.LaunchSetup;
            }
            else
            {
                _addStatus = "Enroll failed: " + (_enrollTask.Exception?.GetBaseException().Message ?? "unknown error");
            }
            _enrollTask = null;
        }

        if (!string.IsNullOrEmpty(_addStatus))
        {
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.AlignTextToFramePadding();
            Widgets.Text(_addStatus, Theme.TextMuted, Theme.Caption);
        }

        Widgets.Gap(Theme.Space.Md);

        // Names of ticked candidates still missing a save folder — enrollment is gated on them
        // (Phase 1.3): a game cannot be enrolled into the broken state that produced the Deck failures.
        var missing = _selected
            .Where(i => i >= 0 && i < _candidates.Count && string.IsNullOrEmpty(_candidates[i].SuggestedSaveDir))
            .Select(i => _candidates[i].Name)
            .ToList();

        // Only filters that would show something are offered: an empty "Heroic" pill on a Deck with
        // no Heroic install is one more thing to nav past that can never do anything. Suggested and
        // All always render, so the row never disappears.
        var filters = Enum.GetValues<AddFilter>()
            .Select(f => (Filter: f, Count: _candidates.Count(c => MatchesFilter(c, f))))
            .Where(x => x.Count > 0 || x.Filter is AddFilter.Suggested or AddFilter.All)
            .ToList();

        if (_candidates.Count > 0)
        {
            foreach (var (f, count) in filters)
            {
                if (f != filters[0].Filter) ImGui.SameLine(0, Theme.Space.Sm);
                var kind = _addFilter == f ? Widgets.ButtonKind.Primary : Widgets.ButtonKind.Ghost;
                if (Widgets.PillButton($"{FilterLabel(f)}  {count}", kind))
                    _addFilter = f;
            }
            Widgets.Gap(Theme.Space.Sm);

            // Path detection, a second row: always shown (unlike the source row, it is not specific
            // to one source), and counted against the source-filtered set so "Detected"/"Not
            // detected" describe what the row above is already narrowed to.
            var sourceFiltered = Enumerable.Range(0, _candidates.Count)
                .Where(i => MatchesFilter(_candidates[i], _addFilter))
                .ToList();
            Widgets.Text("Path", Theme.TextDim, Theme.Caption);
            foreach (var m in Enum.GetValues<PathMode>())
            {
                ImGui.SameLine(0, Theme.Space.Sm);
                var pillLabel = m == PathMode.All
                    ? PathModeLabel(m)
                    : $"{PathModeLabel(m)}  {sourceFiltered.Count(i => MatchesPath(_candidates[i], m))}";
                var kind = _pathMode == m ? Widgets.ButtonKind.Primary : Widgets.ButtonKind.Ghost;
                if (Widgets.PillButton(pillLabel, kind)) _pathMode = m;
            }
            Widgets.Gap(Theme.Space.Sm);
        }

        var shown = Enumerable.Range(0, _candidates.Count)
            .Where(i => MatchesFilter(_candidates[i], _addFilter) && MatchesPath(_candidates[i], _pathMode))
            .ToList();

        // The section header is drawn BEFORE the list height is measured: measuring first and then
        // adding a header underflows the child by exactly the header's height, which clipped the
        // action bar off the bottom of the screen.
        Widgets.SectionHeader(_candidates.Count > 0
            ? $"Discovered games ({shown.Count} of {_candidates.Count})"
            : "Discovered games");

        // The action bar is pinned to the bottom so it never scrolls out of reach on a long list —
        // on a gamepad, hunting for a submit button below the fold is the worst case. The reserve
        // covers the button, the gap above it, and ImGui's item spacing on both.
        var buttonH = ImGui.GetTextLineHeight() + (Theme.Space.Sm + 2f) * 2;
        var barH = buttonH + Theme.Space.Sm + ImGui.GetStyle().ItemSpacing.Y * 2 + Theme.Space.Sm;
        var listH = MathF.Max(120f, ImGui.GetContentRegionAvail().Y - barH);

        // AlwaysUseWindowPadding: a child without it gets ZERO padding, so these full-width rows would
        // touch both edges and have their focus rings clipped (Theme.Layout.FocusClearance).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(Theme.Layout.FocusClearance, Theme.Layout.FocusClearance));
        ImGui.BeginChild("candidates", new Vector2(0, listH),
            ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NavFlattened);
        NavDebug.PushScope("candidates");
        if (shown.Count > 0)
        {
            // Absolute indices throughout: _selected and Enroller both index into _candidates, so a
            // filter must never renumber anything — hiding a row would otherwise silently enroll a
            // different game than the one that was ticked.
            foreach (var i in shown) DrawCandidateRow(i);
        }
        else if (_candidates.Count > 0)
        {
            var pathSuffix = _pathMode == PathMode.All ? "" : $" + {PathModeLabel(_pathMode)}";
            // Plain quotes, not curly ones: the embedded font's glyph atlas is ASCII + Latin-1 only
            // (Theme.LoadFonts), so U+201C/U+201D rendered as missing-glyph boxes here.
            Widgets.Text($"No games match \"{FilterLabel(_addFilter)}{pathSuffix}\". Choose All to see every one.",
                Theme.TextMuted);
        }
        else
        {
            Widgets.Text("Scan to find games with Proton prefixes on this device.", Theme.TextMuted);
        }
        NavDebug.PopScope();
        ImGui.EndChild();
        NavDebug.NoteContainer("candidates");
        ImGui.PopStyleVar();

        Widgets.Gap(Theme.Space.Sm);

        bool blocked = _selected.Count == 0 || missing.Count > 0 || _enrollTask is { IsCompleted: false };
        var label = _selected.Count > 0 ? $"Enroll {_selected.Count} selected" : "Enroll selected";
        if (Widgets.PillButton(label, Widgets.ButtonKind.Primary, Icons.Check, enabled: !blocked))
            _enrollTask = Enroller.EnrollAsync(_config, new List<ScanCandidate>(_candidates), _selected.ToArray());

        if (missing.Count > 0)
        {
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.AlignTextToFramePadding();
            Widgets.Text("Set a save folder for: " + string.Join(", ", missing),
                Theme.AccentAmberLt, Theme.Caption);
        }
    }

    private void DrawCandidateRow(int i)
    {
        var c = _candidates[i];
        bool alreadyTracked = _config.FindGame(c.Name) is not null;
        bool hasFolder = !string.IsNullOrEmpty(c.SuggestedSaveDir);

        ImGui.PushID(i);
        bool ticked = _selected.Contains(i);

        ImGui.BeginGroup();
        if (Widgets.CheckRow("sel", ref ticked, enabled: !alreadyTracked))
        {
            if (ticked) _selected.Add(i); else _selected.Remove(i);
        }
        ImGui.SameLine(0, Theme.Space.Md);

        ImGui.BeginGroup();
        Widgets.Text(c.Name, alreadyTracked ? Theme.TextMuted : Theme.TextPrimary, Theme.BodyStrong);
        ImGui.SameLine(0, Theme.Space.Sm);
        ImGui.AlignTextToFramePadding();
        Widgets.Text($"[{c.Source}]", Theme.TextDim, Theme.Caption);

        if (alreadyTracked)
        {
            ImGui.SameLine(0, Theme.Space.Sm);
            Widgets.Badge("already tracked", Theme.AccentGreen, Icons.Check);
        }
        else if (!hasFolder)
        {
            ImGui.SameLine(0, Theme.Space.Sm);
            Widgets.Badge("no save folder", Theme.AccentAmber, Icons.AlertTriangle);
        }

        if (!alreadyTracked)
        {
            Widgets.Text(hasFolder ? c.SuggestedSaveDir! : "Pick where this game keeps its saves.",
                hasFolder ? Theme.TextMuted : Theme.AccentAmberLt, Theme.Caption);
            if (Widgets.PillButton(hasFolder ? "Change folder" : "Set save folder",
                    hasFolder ? Widgets.ButtonKind.Ghost : Widgets.ButtonKind.Secondary, Icons.Folder))
                EnterSetFolder(i);
        }
        ImGui.EndGroup();
        ImGui.EndGroup();

        Widgets.Gap(Theme.Space.Sm);
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        dl.AddLine(p, p + new Vector2(ImGui.GetContentRegionAvail().X, 0),
            ImGui.ColorConvertFloat4ToU32(Theme.BgRowSep), 1f);
        Widgets.Gap(Theme.Space.Sm);

        ImGui.PopID();
    }

    private void EnterSetFolder(int candidateId)
    {
        _folderTargetId = candidateId;
        var c = _candidates[candidateId];
        // Seed the browser inside the game's own prefix so a Deck user never hunts the Wine tree:
        // existing save guess, then the deepest existing dir under the prefix, then the roots.
        _browsePath = (!string.IsNullOrEmpty(c.SuggestedSaveDir) && Directory.Exists(c.SuggestedSaveDir))
            ? c.SuggestedSaveDir!
            : SaveLocker.Shared.WinePrefix.BrowseStart(c.PrefixPath) ?? "";
        _lastListedPath = null;
        _screen = Screen.SetFolder;
    }

    private void DrawSetFolder()
    {
        if (_folderTargetId < 0 || _folderTargetId >= _candidates.Count)
        {
            _screen = Screen.AddGame;
            return;
        }

        var c = _candidates[_folderTargetId];
        Widgets.Text($"Save folder for {c.Name}", Theme.TextPrimary, Theme.Title);
        if (!string.IsNullOrEmpty(c.PrefixPath))
            Widgets.TextWrapped(
                "Opened inside this game's Proton prefix. Saves usually sit under Documents, "
                + "AppData/Roaming or AppData/LocalLow. The right pane lists files in the current "
                + "folder so you can confirm the save is really here.",
                Theme.TextMuted);
        Widgets.Gap(Theme.Space.Sm);

        if (_lastListedPath != _browsePath)
        {
            _listing = _browser.List(string.IsNullOrEmpty(_browsePath) ? null : _browsePath);
            _files = (_listing is null ? null : _browser.ListFiles(_browsePath)) ?? Array.Empty<string>();
            _lastListedPath = _browsePath;
            _focusFirstEntry = true; // re-seed the cursor onto the first row of the new folder
        }

        if (_listing is null)
        {
            Widgets.Banner("unreadable", "Folder not available",
                "That folder is not readable, or is outside the browsable roots.",
                Theme.AccentAmber, Icons.AlertTriangle);
            _browsePath = "";
            return;
        }

        Widgets.Badge(string.IsNullOrEmpty(_listing.Path) ? "(roots)" : _listing.Path,
            Theme.TextMuted, Icons.HardDrive, mono: true);
        Widgets.Gap(Theme.Space.Sm);

        var avail = ImGui.GetContentRegionAvail();
        var barH = ImGui.GetTextLineHeight() + Theme.Space.Sm * 2 + Theme.Space.Lg;
        float paneH = MathF.Max(200f, avail.Y - barH);

        // Left: folders, gamepad-navigable. Right: files here, read-only — the confirmation that
        // this is actually the save directory and not an empty parent.
        Widgets.TwoColumn("browse", 0.58f,
            left: () =>
            {
                bool wantFocus = _focusFirstEntry;
                _focusFirstEntry = false;

                if (_listing.Parent is not null || !string.IsNullOrEmpty(_listing.Path))
                {
                    if (wantFocus) { ImGui.SetKeyboardFocusHere(); wantFocus = false; }
                    if (Widgets.ListRow("up", "..", "Up one level", Icons.ChevronUp, chevron: false))
                        _browsePath = _listing.Parent ?? "";
                }
                foreach (var entry in _listing.Entries)
                {
                    if (wantFocus) { ImGui.SetKeyboardFocusHere(); wantFocus = false; }
                    if (Widgets.ListRow($"d{entry.Path}", entry.Name, null, Icons.Folder))
                        _browsePath = entry.Path;
                }
            },
            right: () =>
            {
                Widgets.SectionHeader(_files.Length == 0 ? "Files here (none)" : $"Files here ({_files.Length})");
                if (_files.Length == 0)
                    Widgets.TextWrapped("This folder has no files directly in it.", Theme.TextDim);
                foreach (var f in _files)
                    Widgets.Text(f, Theme.TextMuted, Theme.Caption);
            },
            height: paneH);

        Widgets.Gap(Theme.Space.Sm);

        bool canUse = !string.IsNullOrEmpty(_listing.Path);
        if (Widgets.PillButton("Use this folder", Widgets.ButtonKind.Primary, Icons.Check, enabled: canUse))
        {
            _candidates[_folderTargetId] = c with { SuggestedSaveDir = _listing.Path };
            _selected.Add(_folderTargetId);
            _screen = Screen.AddGame;
        }
        ImGui.SameLine(0, Theme.Space.Sm);
        if (Widgets.PillButton("Cancel", Widgets.ButtonKind.Ghost)) _screen = Screen.AddGame;
    }

    private void DrawLaunchSetup()
    {
        if (_launch.Command is null)
        {
            Widgets.Banner("nolaunch", "Not available here",
                _launch.Note ?? "No launch command available on this platform.",
                Theme.AccentAmber, Icons.AlertTriangle);
            return;
        }

        Widgets.Text("Steam launch setup", Theme.TextPrimary, Theme.Title);
        Widgets.TextWrapped("Add this to each game's Steam launch options so SaveLocker syncs it.",
            Theme.TextMuted);
        Widgets.Gap(Theme.Space.Md);

        // The command block: mono, accented, on a card. If the clipboard fails this is what the
        // user retypes, so it has to be unambiguous — hence the monospace face.
        Widgets.BeginCard("cmd", new Vector2(0, 0), Theme.BgCard, Theme.ChipBorder);
        Widgets.TextWrapped(_launch.Command, Theme.AccentGreen, Theme.Mono);
        Widgets.EndCard();

        Widgets.Gap(Theme.Space.Md);

        if (Widgets.PillButton("Copy to clipboard", Widgets.ButtonKind.Primary, Icons.Copy))
            _copyResult = SetClipboard(_launch.Command)
                ? "Copied."
                : "Copy failed - use the command above.";

        if (!string.IsNullOrEmpty(_copyResult))
        {
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.AlignTextToFramePadding();
            Widgets.Text(_copyResult,
                _copyResult.StartsWith("Copied") ? Theme.AccentGreen : Theme.AccentAmber,
                Theme.Caption);
        }

        Widgets.Gap(Theme.Space.Lg);
        Widgets.SectionHeader("Notes");

        Note("The command is identical for every game.",
            "Once one game is set up you can paste the same string into any other game's Launch "
            + "Options - SaveLocker is only needed here for the first game on a device.");
        Note("Use the full path shown above.",
            "Game Mode does not put ~/.local/bin on PATH.");
        Note("Tick \"Force the use of a specific Steam Play compatibility tool\".",
            "On a non-Steam shortcut, without this Proton never creates a prefix.");
    }

    /// <summary>
    /// Drain the fire-and-forget requests <see cref="PollConflictState"/> and
    /// <see cref="EnsureVersionLoaded"/> start. Every dictionary write in this file happens here, on
    /// the render thread — never inside an async continuation — the same discipline the scan/enroll
    /// tasks elsewhere in this screen already follow, so there is no cross-thread field mutation.
    /// </summary>
    private void PollConflictState()
    {
        var now = Environment.TickCount64;
        if (Connected && _conflictsTask is null &&
            (now - _conflictsReadAt > ConflictsPollMs || _conflictsReadAt == 0))
        {
            // Stamped before the request completes so a slow or failed round trip does not retry
            // every single frame while it is in flight.
            _conflictsReadAt = now;
            _conflictsTask = FetchOpenConflictsAsync();
        }
        if (_conflictsTask is { IsCompletedSuccessfully: true } done)
        {
            _openConflicts = done.Result;
            PruneClosedConflictState(_openConflicts);
            _conflictsTask = null;
        }
        else if (_conflictsTask is { IsCompleted: true } incomplete)
        {
            // Covers both Faulted and Canceled — an unhandled OperationCanceledException (e.g. from
            // ServerHttp's HttpClient hitting its own default 100s timeout on an unresponsive
            // self-hosted server) completes the task as Canceled, not Faulted, and previously fell
            // through both branches here, leaving _conflictsTask non-null forever and freezing every
            // future poll (the `is null` guard above never passes again).
            _conflictsError = incomplete.Exception?.GetBaseException().Message
                ?? "Could not check for conflicts.";
            _conflictsTask = null;   // best-effort — retried on the next poll
        }

        DrainVersionTasks(_versionTasks, _versions);
        DrainVersionTasks(_versionStatsTasks, _versionStats);

        if (_resolveTask is { IsCompleted: true } resolved)
        {
            if (resolved.IsCompletedSuccessfully)
            {
                var (ok, error) = resolved.Result;
                if (ok)
                {
                    _conflictKeepBoth.Remove(_resolvingConflictId!.Value);
                    // Keeping local makes this machine the winner, which excludes it from the
                    // server's post-resolve pull fan-out (SyncService.ResolveConflictAsync) on the
                    // assumption it already advanced its own parent pointer — mirrors AgentCli.cs's
                    // resolve-conflict --keep local. Without this, the next sync cycle pushes with a
                    // stale ParentVersionId; it self-heals to NoChange, but wastes a round trip.
                    if (_resolvingWinningVersionId == _resolvingConflict!.VersionBId)
                    {
                        var game = _config.Games.FirstOrDefault(g => g.GameId == _resolvingConflict.GameId);
                        if (game is not null)
                        {
                            game.LastKnownVersionId = _resolvingWinningVersionId;
                            game.LastSyncedHash = SaveArchive.HashDirectory(game.SaveDirectory, game.ExcludeGlobs);
                            _config.SaveGameSyncState(game);
                        }
                    }
                    _conflictsReadAt = 0;   // re-poll now so the resolved conflict drops off at once
                }
                else _resolveError = error ?? "Could not resolve the conflict.";
            }
            else
            {
                _resolveError = resolved.Exception?.GetBaseException().Message ?? "Could not resolve the conflict.";
            }
            _resolveTask = null;
            _resolvingConflictId = null;
            _resolvingConflict = null;
            _resolvingWinningVersionId = null;
        }
    }

    /// <summary>Shared drain for the version and stats task dictionaries: cache a successful result,
    /// otherwise mark the id for a cooldown so <see cref="EnsureVersionLoaded"/> doesn't refire the
    /// fetch every single frame on a 404 or transient failure.</summary>
    private void DrainVersionTasks<T>(Dictionary<Guid, Task<T?>> tasks, Dictionary<Guid, T> cache)
        where T : class
    {
        if (tasks.Count == 0) return;
        var now = Environment.TickCount64;
        foreach (var id in tasks.Keys.ToArray())
        {
            var t = tasks[id];
            if (!t.IsCompleted) continue;
            if (t.IsCompletedSuccessfully && t.Result is { } v) cache[id] = v;
            else _versionRetryAt[id] = now + VersionRetryCooldownMs;
            tasks.Remove(id);
        }
    }

    /// <summary>Drop cached version/stats/retry data for any version no longer referenced by an open
    /// conflict, and any "keep both" choice for a conflict that is no longer open — otherwise these
    /// caches grow for the life of the process regardless of which surface (this screen, the CLI, or
    /// the dashboard) actually resolves the conflict.</summary>
    private void PruneClosedConflictState(List<ConflictDto> stillOpen)
    {
        var liveConflictIds = stillOpen.Select(c => c.Id).ToHashSet();
        foreach (var id in _conflictKeepBoth.Keys.ToArray())
            if (!liveConflictIds.Contains(id)) _conflictKeepBoth.Remove(id);

        var liveVersionIds = stillOpen.SelectMany(c => new[] { c.VersionAId, c.VersionBId }).ToHashSet();
        foreach (var id in _versions.Keys.ToArray())
            if (!liveVersionIds.Contains(id)) _versions.Remove(id);
        foreach (var id in _versionStats.Keys.ToArray())
            if (!liveVersionIds.Contains(id)) _versionStats.Remove(id);
        foreach (var id in _versionRetryAt.Keys.ToArray())
            if (!liveVersionIds.Contains(id)) _versionRetryAt.Remove(id);
    }

    private async Task<List<ConflictDto>> FetchOpenConflictsAsync() =>
        await Api().GetOpenConflictsForMachineAsync(_config.MachineId);

    /// <summary>Fire off a version + stats fetch the first time a conflict card asks for this id,
    /// unless it just failed and is still on its retry cooldown.</summary>
    private void EnsureVersionLoaded(Guid versionId)
    {
        if (_versionRetryAt.TryGetValue(versionId, out var retryAt) && Environment.TickCount64 < retryAt)
            return;
        if (!_versions.ContainsKey(versionId) && !_versionTasks.ContainsKey(versionId))
            _versionTasks[versionId] = Api().GetVersionAsync(versionId);
        if (!_versionStats.ContainsKey(versionId) && !_versionStatsTasks.ContainsKey(versionId))
            _versionStatsTasks[versionId] = Api().GetVersionStatsAsync(versionId);
    }

    private void ResolveConflict(ConflictDto conflict, Guid winningVersionId)
    {
        _resolvingConflictId = conflict.Id;
        _resolvingConflict = conflict;
        _resolvingWinningVersionId = winningVersionId;
        _resolveError = null;
        var keepBoth = _conflictKeepBoth.GetValueOrDefault(conflict.Id);
        _resolveTask = Api().ResolveConflictAsync(conflict.Id, winningVersionId, keepBoth);
    }

    /// <summary>
    /// The direct answer to "a conflict popup like the Decky one, but in the native Linux UI, with
    /// no Decky installed" (tasks/conflict-resolution-ui/plan.md, Phase 8). A screen rather than a
    /// modal, deliberately: leaving it via the rail or B is already the "decide later" back-out the
    /// plan calls for, so nothing here needs to duplicate that as a second button. Keep Local / Keep
    /// Cloud resolve immediately on press (mirrors the sync-time pop-up's 'immediate' mode, not the
    /// confirm-then-resolve two-step the dashboard/agent-ui page uses) — this screen is not a queue,
    /// so there is nothing to advance afterward.
    /// </summary>
    private void DrawConflicts()
    {
        if (!Connected)
        {
            Widgets.Banner("conflictsblocked", "This device is not enrolled",
                "Conflicts can only be checked once this device is enrolled. Run  savelocker enroll "
                + "--file <policy.json>  once in Desktop Mode, then come back here.",
                Theme.AccentAmber, Icons.AlertTriangle);
            return;
        }

        Widgets.Text("Open conflicts", Theme.TextPrimary, Theme.Title);
        Widgets.TextWrapped(
            "This device and the cloud both changed the same save before syncing. Pick which one to "
            + "keep - the other is never deleted, just set aside.",
            Theme.TextMuted);
        Widgets.Gap(Theme.Space.Md);

        if (!string.IsNullOrEmpty(_resolveError))
        {
            if (Widgets.Banner("resolveerr", "Could not resolve", _resolveError, Theme.AccentAmber,
                    Icons.AlertTriangle, dismissible: true))
                _resolveError = null;
            Widgets.Gap(Theme.Space.Md);
        }

        if (!string.IsNullOrEmpty(_conflictsError))
        {
            if (Widgets.Banner("conflictserr", "Could not check for conflicts", _conflictsError,
                    Theme.AccentAmber, Icons.AlertTriangle, dismissible: true))
                _conflictsError = null;
            Widgets.Gap(Theme.Space.Md);
        }

        if (_openConflicts.Count == 0)
        {
            Widgets.Gap(Theme.Space.Xl);
            Icons.Draw(Icons.Cloud, 40f, Theme.AccentGreen);
            Widgets.Gap(Theme.Space.Sm);
            Widgets.Text("Every tracked game's save matches the cloud.", Theme.TextPrimary, Theme.BodyStrong);
            Widgets.Gap(Theme.Space.Xs);
            Widgets.TextWrapped(
                "If this device and the cloud both change the same save before syncing, the choice "
                + "will show up here.",
                Theme.TextMuted, Theme.Caption);
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(Theme.Layout.FocusClearance, Theme.Layout.FocusClearance));
        ImGui.BeginChild("conflictlist", new Vector2(0, 0), ImGuiChildFlags.None,
            ImGuiWindowFlags.NavFlattened);
        NavDebug.PushScope("conflictlist");
        foreach (var c in _openConflicts) DrawConflictCard(c);
        NavDebug.PopScope();
        ImGui.EndChild();
        NavDebug.NoteContainer("conflictlist");
        ImGui.PopStyleVar();
    }

    private void DrawConflictCard(ConflictDto c)
    {
        ImGui.PushID(c.Id.ToString());

        EnsureVersionLoaded(c.VersionAId);
        EnsureVersionLoaded(c.VersionBId);
        _versions.TryGetValue(c.VersionAId, out var cloudVersion);
        _versions.TryGetValue(c.VersionBId, out var localVersion);
        _versionStats.TryGetValue(c.VersionAId, out var cloudStats);
        _versionStats.TryGetValue(c.VersionBId, out var localStats);

        var gameName = _config.Games.FirstOrDefault(g => g.GameId == c.GameId)?.Name ?? c.GameId.ToString();
        bool resolving = _resolvingConflictId == c.Id;
        bool anyResolving = _resolvingConflictId is not null;

        Widgets.BeginCard("card", new Vector2(0, 0), Theme.BgCard, Theme.Border);

        Widgets.Text(gameName, Theme.TextPrimary, Theme.BodyStrong);
        ImGui.SameLine(0, Theme.Space.Sm);
        ImGui.AlignTextToFramePadding();
        Widgets.Badge("CONFLICT", Theme.AccentAmber, Icons.AlertTriangle);
        if (c.Escalated)
        {
            ImGui.SameLine(0, Theme.Space.Sm);
            ImGui.AlignTextToFramePadding();
            Widgets.Text("Overdue - unresolved for over six hours.", Theme.AccentRed, Theme.Caption);
        }
        Widgets.Gap(Theme.Space.Sm);

        bool newerIsCloud = cloudVersion is not null && localVersion is not null
            && cloudVersion.CreatedAt > localVersion.CreatedAt;
        bool newerIsLocal = cloudVersion is not null && localVersion is not null
            && localVersion.CreatedAt > cloudVersion.CreatedAt;

        var panelWidth = (ImGui.GetContentRegionAvail().X - Theme.Space.Md) / 2f;

        DrawConflictSide("local", "This device", Icons.HardDrive, localVersion, localStats, newerIsLocal,
            "This is the machine you're using right now.", anyResolving, panelWidth,
            () => ResolveConflict(c, c.VersionBId));
        ImGui.SameLine(0, Theme.Space.Md);
        DrawConflictSide("cloud", "The cloud", Icons.Cloud, cloudVersion, cloudStats, newerIsCloud,
            cloudVersion is not null ? $"Last updated from \"{cloudVersion.MachineName}\"" : null,
            anyResolving, panelWidth, () => ResolveConflict(c, c.VersionAId));

        Widgets.Gap(Theme.Space.Sm);
        var keepBoth = _conflictKeepBoth.GetValueOrDefault(c.Id);
        Widgets.Toggle("Also keep the other one as a backup", ref keepBoth);
        _conflictKeepBoth[c.Id] = keepBoth;

        if (resolving)
        {
            Widgets.Gap(Theme.Space.Xs);
            Widgets.Text("Resolving...", Theme.TextMuted, Theme.Caption);
        }

        Widgets.EndCard();
        Widgets.Gap(Theme.Space.Md);
        ImGui.PopID();
    }

    /// <summary>One side of a conflict card — machine, "12m ago", size/file count, and its own Keep
    /// button, which resolves immediately on press (see <see cref="DrawConflicts"/>'s doc comment).</summary>
    private void DrawConflictSide(string id, string label, Icons.Glyph icon, SaveVersionDto? version,
        VersionStatsDto? stats, bool isNewer, string? caption, bool anyResolving,
        float width, Action onKeep)
    {
        ImGui.PushID(id);
        Widgets.BeginCard("side", new Vector2(width, 0), Theme.BgTableHd, Theme.Border);

        // No AlignTextToFramePadding here: the icon's Dummy is exactly one text line tall (see
        // Icons.Draw's size argument below), so it is already level with the label without it. That
        // call is for lining plain text up against a *taller*, frame-padded sibling (see Toggle/
        // HintLabel) - adding it here when nothing on the line is taller just pushes the label down
        // by FramePadding.y for no reason, which is what put the icon and label visibly out of line.
        Icons.Draw(icon, ImGui.GetTextLineHeight(), Theme.TextMuted);
        ImGui.SameLine(0, Theme.Space.Sm);
        Widgets.Text(label, Theme.TextPrimary, Theme.BodyStrong);
        if (isNewer)
        {
            ImGui.SameLine(0, Theme.Space.Sm);
            ImGui.AlignTextToFramePadding();
            Widgets.Badge("newer", Theme.AccentGreen);
        }

        Widgets.Gap(Theme.Space.Xs);
        if (version is not null)
        {
            Widgets.Text(FormatAgo(DateTime.UtcNow - version.CreatedAt), Theme.TextPrimary, Theme.Mono);
            var fileCount = stats is { FileCount: var fc } ? $"{fc} file{(fc == 1 ? "" : "s")} - " : "";
            Widgets.Text(fileCount + FormatBytes(version.Size), Theme.TextMuted, Theme.Caption);
            if (!string.IsNullOrEmpty(caption))
                Widgets.TextWrapped(caption, Theme.TextDim, Theme.Caption);
        }
        else
        {
            Widgets.Text("Loading...", Theme.TextDim, Theme.Caption);
        }

        Widgets.Gap(Theme.Space.Sm);
        bool enabled = version is not null && !anyResolving;
        if (Widgets.PillButton($"Keep {label}", Widgets.ButtonKind.Primary, Icons.Check, enabled: enabled))
            onKeep();

        Widgets.EndCard();
        ImGui.PopID();
    }

    private static void Note(string title, string body)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var lineH = ImGui.GetTextLineHeight();
        dl.AddCircleFilled(p + new Vector2(4f, lineH / 2f), 3f,
            ImGui.ColorConvertFloat4ToU32(Theme.AccentGreen), 10);

        ImGui.Indent(Theme.Space.Lg);
        Widgets.Text(title, Theme.TextPrimary, Theme.BodyStrong);
        Widgets.TextWrapped(body, Theme.TextMuted, Theme.Caption);
        ImGui.Unindent(Theme.Space.Lg);
        Widgets.Gap(Theme.Space.Sm);
    }

    /// <summary>
    /// Replay queued gamepad presses as single-frame ImGui nav pulses. Silk's ImGuiController wires
    /// keyboard and mouse only, so this glue is what makes D-pad + A/B drive focus (§3.3.2). Analog
    /// nav is suppressed so a resting trackpad-joystick cannot nudge focus.
    /// </summary>
    private void FeedImGuiNav()
    {
        var io = ImGui.GetIO();

        foreach (var k in new[]
                 {
                     ImGuiKey.GamepadLStickUp, ImGuiKey.GamepadLStickDown,
                     ImGuiKey.GamepadLStickLeft, ImGuiKey.GamepadLStickRight,
                 })
            io.AddKeyAnalogEvent(k, false, 0f);

        ImGuiKey[] fire;
        lock (_navLock)
        {
            // Held-to-repeat: a D-pad direction still down past the delay emits a pulse every tick,
            // so a long folder list scrolls without tapping dozens of times.
            var now = Environment.TickCount64;
            foreach (var (key, since) in _heldSince)
            {
                if (now - since < RepeatDelayMs) continue;
                if (now - _lastRepeat[key] < RepeatRateMs) continue;
                _navQueue.Add(key);
                _lastRepeat[key] = now;
            }

            fire = _navQueue.Distinct().ToArray();
            _navQueue.Clear();
            _navLeftFired = fire.Contains(ImGuiKey.GamepadDpadLeft);
            _navRightFired = fire.Contains(ImGuiKey.GamepadDpadRight);
            _navBackFired = fire.Contains(ImGuiKey.GamepadFaceRight);
        }

        // Release last frame's pulses, then press this frame's — a clean down/up per physical press.
        // Releasing unconditionally means a key that fires two frames running still reads as two
        // distinct presses (release+press queue as separate events ImGui processes in order).
        foreach (var k in _navDownLastFrame)
            io.AddKeyEvent(k, false);
        foreach (var k in fire)
            io.AddKeyEvent(k, true);
        _navDownLastFrame = fire;

        // A D-pad press takes the cursor back from the pointer. Done here rather than in
        // TrackCursorOwner so the change applies to the frame the press is fed, not the one after.
        if (fire.Length > 0) Widgets.PointerDrives = false;

        NavDebug.NoteKeys(fire);
    }

    /// <summary>
    /// Park a synthetic pointer at a fixed position for the whole run — <c>--pointer X,Y</c>.
    ///
    /// A development affordance, and the only way to reproduce a Deck symptom off-device. WSLg leaves
    /// the pointer outside the window unless a person is holding it there, so hover is dead in every
    /// unattended capture; gamescope always hands the app a pointer position whether or not anyone has
    /// touched the trackpad. Parking one makes the difference testable in a screenshot.
    ///
    /// It is deliberately STATIONARY: it never generates a MouseDelta, which is exactly the case that
    /// used to paint a second selector nobody could move.
    /// </summary>
    private static Vector2? _pointerPark;

    private static Vector2? ParsePointer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split(',');
        if (parts.Length == 2 &&
            float.TryParse(parts[0], out var x) && float.TryParse(parts[1], out var y))
            return new Vector2(x, y);

        Console.Error.WriteLine($"Ignoring bad --pointer '{raw}' — expected X,Y.");
        return null;
    }

    /// <summary>
    /// Decide which input owns the cursor this frame — see <see cref="Widgets.PointerDrives"/>.
    ///
    /// Starts false: a Deck is a gamepad first, and gamescope hands the app a pointer position
    /// whether or not anyone has touched the trackpad. Treating that stationary pointer as live is
    /// what painted a highlight on whatever it happened to be resting over — a second selector that
    /// the D-pad did not move and A did not activate.
    /// </summary>
    private static void TrackCursorOwner()
    {
        var io = ImGui.GetIO();

        // The park is queued rather than assigned to io.MousePos, because NewFrame is where
        // HoveredWindow is resolved and an assignment made after it can never make a CHILD window
        // report hover — only the queue gets there in time.
        //
        // Returning early is the important half. The injection races Silk, which writes the real
        // pointer position during the same Update, so the resolved position alternates and
        // MouseDelta is non-zero on nearly every frame. A parked pointer is stationary by
        // definition, so that delta is an artifact of the harness and must never be read as the
        // user reaching for the trackpad — which is precisely the thing under test.
        if (_pointerPark is { } park)
        {
            io.AddMousePosEvent(park.X, park.Y);
            return;
        }

        // ImGui seeds MousePos at -FLT_MAX, so the first frame with a real position produces a
        // delta the size of the float range. That is the backend arriving, not the user moving.
        var d = io.MouseDelta;
        const float Absurd = 1e5f;
        bool moved = MathF.Abs(d.X) < Absurd && MathF.Abs(d.Y) < Absurd &&
                     (MathF.Abs(d.X) > 0.5f || MathF.Abs(d.Y) > 0.5f);

        if (moved || ImGui.IsMouseClicked(ImGuiMouseButton.Left) ||
            ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            Widgets.PointerDrives = true;
    }

    /// <summary>
    /// Copy to the system clipboard via SDL so the string crosses into Steam's Launch Options field
    /// (§3.3.3). SDL is already initialised by the window; SetClipboardText is process-global.
    /// </summary>
    private static unsafe bool SetClipboard(string text)
    {
        try
        {
            var sdl = SdlApi.GetApi();
            var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
            fixed (byte* p = bytes)
                return sdl.SetClipboardText(p) == 0;
        }
        catch { return false; }
    }

    // ImGui.NET's Text* helpers treat their argument as a printf FORMAT string, so any dynamic text
    // containing '%' (e.g. the launch command's "%command%") corrupts the output — the "?????" field.
    // TextUnformatted never formats; these route colour/wrap through it.
    private static void TextWrap(string s)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(s);
        ImGui.PopTextWrapPos();
    }

    private static void TextCol(Vector4 colour, string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextUnformatted(s);
        ImGui.PopStyleColor();
    }

    private static void TextDis(string s) => TextCol(Theme.TextMuted, s);

    private static string FormatAgo(TimeSpan ago)
    {
        if (ago.TotalSeconds < 60) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }
}
