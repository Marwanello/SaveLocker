using System.Net.Http.Json;
using System.Numerics;
using ImGuiNET;
using SaveLocker.Shared;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// The Deck's tracked-games surfaces (checkpoint-ui Phase 14): the Tracked games list with cover art, one
/// game's page, and the Activity screen with its offline queue. Everything here that changes anything asks
/// the daemon over its own local API — the same rule <see cref="SyncNowAsync"/> follows: this process never
/// builds a sync engine of its own and never writes config.json for a game.
/// </summary>
sealed partial class UiApp
{
    // ── Talking to the daemon ────────────────────────────────────────────────────────────────

    private HttpClient? _daemonHttp;

    /// <summary>One client for every call. Its own timeout is infinite on purpose: a per-game sync holds its
    /// request open for the whole push, and each short read passes its own budget instead.</summary>
    private HttpClient DaemonClient()
    {
        if (_daemonHttp is not null) return _daemonHttp;
        var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{_apiPort}/"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Add(LocalAuth.HeaderName, _localAuth.Token);
        return _daemonHttp = http;
    }

    private async Task<T?> DaemonGet<T>(string path, int seconds = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        return await DaemonClient().GetFromJsonAsync<T>(path, cts.Token);
    }

    // ── Cover art ────────────────────────────────────────────────────────────────────────────
    // The daemon's /api/games/{id}/art proxy already serves it (agent-ui's GameArt uses it too); what this
    // adds is a decode into a GL texture for ImGui.Image. StbImageSharp is pure C#, so there is no native
    // library to ship. Art is fetched four at a time, decoded on the render thread (a GL context belongs to
    // it), and released when the user leaves the screens that show it — a Deck has little to spare.

    private sealed class ArtSlot
    {
        public Task<byte[]?>? Fetch;
        public uint Texture;
        public float Aspect = 1f;
        public bool Done;
    }

    private readonly Dictionary<Guid, ArtSlot> _art = new();
    private int _artInFlight;

    /// <summary>The cover texture for a game once it has loaded, else default (the row draws its initial).
    /// Asking is what starts the fetch.</summary>
    private (IntPtr Texture, float Aspect) Cover(Guid id)
    {
        if (!_art.TryGetValue(id, out var slot)) _art[id] = slot = new ArtSlot();
        if (slot.Texture != 0) return ((IntPtr)slot.Texture, slot.Aspect);
        if (slot.Fetch is null && !slot.Done && _artInFlight < 4)
        {
            Interlocked.Increment(ref _artInFlight);
            slot.Fetch = FetchArt(id);
        }
        return (default, 1f);
    }

    private async Task<byte[]?> FetchArt(Guid id)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var res = await DaemonClient().GetAsync($"api/games/{id}/art?kind=grid&w=96", cts.Token);
            return res.IsSuccessStatusCode ? await res.Content.ReadAsByteArrayAsync(cts.Token) : null;
        }
        catch { return null; }
        finally { Interlocked.Decrement(ref _artInFlight); }
    }

    private unsafe void UploadArt(ArtSlot slot, byte[] bytes)
    {
        try
        {
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            var tex = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, tex);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* p = image.Data)
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height,
                    0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            slot.Texture = tex;
            slot.Aspect = image.Height == 0 ? 1f : (float)image.Width / image.Height;
        }
        catch
        {
            // Not an image, or one this decoder cannot read: the row keeps its initial. Never fatal.
        }
    }

    private void ReleaseArt()
    {
        if (_gl is null) { _art.Clear(); return; }
        foreach (var slot in _art.Values)
            if (slot.Texture != 0) _gl.DeleteTexture(slot.Texture);
        _art.Clear();
    }

    private void OnScreenChanged(Screen target)
    {
        // Covers are only drawn on these; leaving them frees the textures (the Games list refetches — the
        // daemon caches art URLs, so that is one small request per game, not a search).
        if (target is not (Screen.Games or Screen.Game or Screen.SetFolder)) ReleaseArt();
        _sizesLoaded = false;   // measured again the next time the list is drawn
        if (target == Screen.Games) LoadGameSizes();
        if (target == Screen.Activity) _queueReadAt = 0;
    }

    // ── Per-game folder sizes (a directory walk, so once on entering the list — never per frame) ──

    private readonly Dictionary<Guid, long> _sizes = new();
    private readonly Dictionary<Guid, Task<long>> _sizeTasks = new();

    private bool _sizesLoaded;

    private void LoadGameSizes()
    {
        _sizesLoaded = true;
        foreach (var g in _config.Games.ToList())
        {
            if (_sizeTasks.ContainsKey(g.GameId)) continue;
            // Every folder of the game that is on this Deck, not just the main one.
            var dirs = g.RealRoots(_config.StateDir).Select(r => r.Directory).ToList();
            _sizeTasks[g.GameId] = Task.Run(() => dirs.Sum(FolderSize.Of));
        }
    }

    // ── One place the render thread takes finished background work ───────────────────────────────

    private void PollGameScreens()
    {
        foreach (var slot in _art.Values)
        {
            if (slot.Fetch is not { IsCompleted: true } done) continue;
            slot.Fetch = null;
            slot.Done = true;
            if (done.IsCompletedSuccessfully && done.Result is { Length: > 0 } bytes) UploadArt(slot, bytes);
        }

        foreach (var (id, task) in _sizeTasks.ToList())
            if (task.IsCompleted)
            {
                if (task.IsCompletedSuccessfully) _sizes[id] = task.Result;
                // Finished tasks are dropped so leaving and re-entering the list measures again.
                _sizeTasks.Remove(id);
            }

        if (_gameStateTask is { IsCompleted: true } st)
        {
            _gameStateTask = null;
            _gameState = st.IsCompletedSuccessfully ? st.Result : null;
            _gameStateError = st.IsCompletedSuccessfully ? null : "Could not reach the server.";
        }
        if (_gameVersionsTask is { IsCompleted: true } vt)
        {
            _gameVersionsTask = null;
            _gameVersions = vt.IsCompletedSuccessfully ? vt.Result : null;
        }
        if (_gameSyncTask is { IsCompleted: true } gs)
        {
            _gameSyncTask = null;
            _gameSyncMessage = gs.IsCompletedSuccessfully
                ? gs.Result
                : "Sync failed: " + (gs.Exception?.GetBaseException().Message ?? "unknown error");
            if (_game is not null) LoadGame(_game);   // what the sync just did is what the page should now say
        }
        if (_suggestionsTask is { IsCompleted: true } sg)
        {
            _suggestionsTask = null;
            _suggestions = sg.IsCompletedSuccessfully && sg.Result is { } all && _game is { } shown
                ? all.Where(s => s.GameId == shown.GameId).ToList()
                : new();
        }
        if (_suggestionActionTask is { IsCompleted: true } sa)
        {
            _suggestionActionTask = null;
            _gameSyncMessage = sa.IsCompletedSuccessfully ? sa.Result : sa.Exception?.GetBaseException().Message;
            _diskReadAt = 0;                   // the daemon may just have written a new folder
            if (_game is not null) LoadGame(_game);
        }
        if (_folderTask is { IsCompleted: true } ft)
        {
            _folderTask = null;
            var (ok, error, ask, path, choice) = ft.IsCompletedSuccessfully ? ft.Result : (false, ft.Exception?.GetBaseException().Message, null, null, false);
            if (ok)
            {
                _folderFlag = null;
                _folderError = null;
                _diskReadAt = 0;               // re-read config.json now: the daemon just wrote the new folder
                _gameSyncMessage = "Save folder updated.";
                var target = _folderGame;
                _folderGame = null;
                if (target is not null) { _screen = Screen.Game; _bestContentId = 0; _focusZone = Zone.Rail; }
            }
            else if (ask is not null) _folderFlag = (ask, path!, choice);
            else _folderError = error ?? "Could not set the save folder.";
        }
    }

    // ── Tracked games ────────────────────────────────────────────────────────────────────────

    private void OpenGame(TrackedGame game)
    {
        // Another game's answers must not stand in while this one's load: the page would show the
        // previous game's latest save, size and versions under this game's name until the fetch landed.
        if (_game?.GameId != game.GameId)
        {
            _gameState = null;
            _gameStateError = null;
            _gameVersions = null;
            _suggestions = new();
        }
        _game = game;
        _gameSyncMessage = null;
        LoadGame(game);
        Go(Screen.Game);
    }

    private void DrawGames()
    {
        // The first screen a run opens on never went through Go, so it asks for its own measurements.
        if (!_sizesLoaded) LoadGameSizes();
        DrawLeaseWarnings();
        Widgets.Text("Tracked games", Theme.Fg, Theme.Title);
        var here = _config.Games.Where(g => g.IsEnrolledHere).ToList();
        var elsewhere = _config.Games.Where(g => !g.IsEnrolledHere).ToList();
        Widgets.Text(elsewhere.Count == 0
                ? $"{here.Count} on this Deck - press A to open one"
                : $"{here.Count} on this Deck, {elsewhere.Count} more on the server - press A to open one",
            Theme.Dim, Theme.Caption);
        Widgets.Gap(Theme.Space.Md);

        if (_config.Games.Count == 0)
        {
            Widgets.Text("No games tracked yet.", Theme.Dim);
            Widgets.Gap(Theme.Space.Md);
            if (Widgets.PillButton("Add a game", Widgets.ButtonKind.Primary, Icons.Plus)) Go(Screen.AddGame);
            return;
        }

        // Games with a save folder here come first; the rest exist only on the server until someone
        // picks a folder, so they sit under their own heading instead of looking like synced games.
        if (elsewhere.Count > 0 && here.Count > 0) Widgets.SectionHeader("On this Deck");
        foreach (var g in here) DrawGameRow(g);
        if (elsewhere.Count > 0)
        {
            Widgets.SectionHeader("On the server");
            Widgets.TextWrapped("No save folder on this Deck yet. Open one to choose it.", Theme.Dim, Theme.Caption);
            Widgets.Gap(Theme.Space.Sm);
            foreach (var g in elsewhere) DrawGameRow(g);
        }
    }

    private void DrawGameRow(TrackedGame g)
    {
        var missing = !g.IsEnrolledHere;
        var conflicted = _openConflicts.Any(c => c.GameId == g.GameId);
        var last = g.LastPushAt is { } at ? "synced " + FormatAgo(DateTime.UtcNow - at) : "not pushed yet";
        var size = _sizes.TryGetValue(g.GameId, out var bytes) && bytes > 0 ? FormatBytes(bytes) : null;
        var sub = missing
            ? "no save folder on this Deck"
            : string.Join(" · ", new[] { size, last, g.SaveDirectory }.Where(s => s is not null));

        var (tex, aspect) = Cover(g.GameId);
        // "Synced" only for a game this device has actually synced (a push or a pull recorded its hash):
        // a game added a minute ago has not been, and a green chip would say otherwise.
        var synced = g.LastSyncedHash is not null;
        var pressed = Widgets.ListRow($"gm{g.GameId}", g.Name, sub, chevron: true,
            cover: true, coverTexture: tex, coverAspect: aspect, coverInitial: g.Name,
            chip: conflicted ? "Conflict" : missing ? "Not set up" : synced ? "Synced" : "Not synced yet",
            chipColour: conflicted ? Theme.Accent : missing ? Theme.Watch : synced ? Theme.Safe : Theme.Dim);
        if (pressed) OpenGame(g);
    }

    // ── One game ─────────────────────────────────────────────────────────────────────────────

    private TrackedGame? _game;
    private Task<GameStateDto?>? _gameStateTask;
    private GameStateDto? _gameState;
    private string? _gameStateError;
    private Task<List<SaveVersionDto>?>? _gameVersionsTask;
    private List<SaveVersionDto>? _gameVersions;
    private Task<string>? _gameSyncTask;
    private string? _gameSyncMessage;

    private TrackedGame? _folderGame;
    /// <summary>Which of <see cref="_folderGame"/>'s save folders the browser sets: <c>main</c> or an extra key.</summary>
    private string _folderKey = SaveRoot.PrimaryKey;
    private Task<(bool Ok, string? Error, string? Ask, string? Path, bool Choice)>? _folderTask;
    /// <summary>The daemon's question about a folder: a heuristic flagged it, or (<c>Choice</c>) this Deck's
    /// files and the cloud's copy differ and one has to be kept.</summary>
    private (string Ask, string Path, bool Choice)? _folderFlag;
    private string? _folderError;
    // "Also found": folders the save database lists for this game that exist here and are not synced.
    private Task<List<FolderSuggestionDto>?>? _suggestionsTask;
    private List<FolderSuggestionDto> _suggestions = new();
    private Task<string?>? _suggestionActionTask;

    private void LoadGame(TrackedGame g)
    {
        var id = g.GameId;
        _gameStateTask = DaemonGet<GameStateDto>($"api/games/{id}/state");
        _gameVersionsTask = DaemonGet<List<SaveVersionDto>>($"api/games/{id}/versions");
        _suggestionsTask = DaemonGet<List<FolderSuggestionDto>>($"api/folder-suggestions?gameId={id}");
    }

    private void DrawGame()
    {
        var g = _game;
        if (g is null || _config.Games.All(x => x.GameId != g.GameId)) { Go(Screen.Games); return; }
        // The list may have been re-read from disk since this page opened; show the current entry.
        g = _config.Games.First(x => x.GameId == g.GameId);
        _game = g;

        var conflicted = _openConflicts.Any(c => c.GameId == g.GameId);
        var missing = string.IsNullOrEmpty(g.SaveDirectory);
        var busy = _gameSyncTask is { IsCompleted: false } || _syncNowTask is { IsCompleted: false };

        if (Widgets.PillButton("Back to games", Widgets.ButtonKind.Ghost, Icons.ChevronLeft)) Go(Screen.Games);
        Widgets.Gap(Theme.Space.Sm);

        // Head: cover, name, state chips.
        {
            var (tex, aspect) = Cover(g.GameId);
            var dl = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            const float box = 72f;
            if (tex != default)
            {
                var (uv0, uv1) = Widgets.CropSquare(aspect);
                dl.AddImageRounded(tex, pos, pos + new Vector2(box), uv0, uv1, Widgets.U32(Vector4.One), 10f);
            }
            else
            {
                dl.AddRectFilled(pos, pos + new Vector2(box), Widgets.U32(Theme.Tile), 10f);
                Theme.PushFont(Theme.Title);
                var initial = g.Name.Length > 0 ? g.Name[..1].ToUpperInvariant() : "?";
                var isz = ImGui.CalcTextSize(initial);
                dl.AddText(pos + (new Vector2(box) - isz) / 2f, Widgets.U32(Theme.Dim), initial);
                Theme.PopFont(Theme.Title);
            }
            ImGui.Dummy(new Vector2(box));
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.BeginGroup();
            Widgets.Text(g.Name, Theme.Fg, Theme.Title);
            if (conflicted) Widgets.Badge("Conflict", Theme.Accent, Icons.GitBranch);
            else if (missing) Widgets.Badge("Needs a save folder", Theme.Watch, Icons.AlertTriangle);
            else if (g.LastSyncedHash is null) Widgets.Badge("Not synced yet", Theme.Dim);
            else Widgets.Badge("In sync as far as this device knows", Theme.Safe, Icons.Check);
            ImGui.EndGroup();
        }
        Widgets.Gap(Theme.Space.Md);

        if (conflicted)
        {
            Widgets.Banner("gameconflict", $"{g.Name} is waiting on you",
                "This device and the cloud both changed. Open Conflicts to keep one.", Theme.Accent, Icons.GitBranch);
            Widgets.Gap(Theme.Space.Sm);
            if (Widgets.PillButton("Choose", Widgets.ButtonKind.Primary, Icons.GitBranch)) Go(Screen.Conflicts);
            Widgets.Gap(Theme.Space.Md);
        }

        // Actions — the daemon's per-game sync (never forced, so a pull still refuses to overwrite local
        // changes and a diverged push still becomes a conflict), plus the existing folder browser.
        // A game with no folder here has nothing to sync, so it is offered a folder instead of buttons
        // that could only refuse (the agent UI does the same).
        if (!missing)
        {
            if (Widgets.PillButton(busy ? "Syncing..." : "Sync this game", Widgets.ButtonKind.Primary, Icons.Sync,
                    enabled: !busy && Connected))
                StartGameSync(g, "sync");
            ImGui.SameLine(0, Theme.Space.Sm);
            if (Widgets.PillButton("Push now", Widgets.ButtonKind.Secondary, enabled: !busy && Connected))
                StartGameSync(g, "push");
            ImGui.SameLine(0, Theme.Space.Sm);
            if (Widgets.PillButton("Pull latest", Widgets.ButtonKind.Secondary, enabled: !busy && Connected))
                StartGameSync(g, "pull");
            ImGui.SameLine(0, Theme.Space.Sm);
        }
        if (Widgets.PillButton(missing ? "Choose save folder" : "Change folder",
                missing ? Widgets.ButtonKind.Primary : Widgets.ButtonKind.Ghost, Icons.Folder)) EnterSetFolderForGame(g);

        // The game's other save folders (tasks/multiple-save-paths). One not set on this Deck still syncs —
        // the agent keeps a copy of it — so choosing its folder here is an offer, never a fault to fix.
        foreach (var p in g.ExtraPaths)
        {
            ImGui.SameLine(0, Theme.Space.Sm);
            if (Widgets.PillButton(p.IsMapped ? $"Change {p.Label ?? p.Key}" : $"Choose {p.Label ?? p.Key} folder",
                    Widgets.ButtonKind.Ghost, Icons.Folder))
                EnterSetFolderForGame(g, p.Key);
        }

        if (!string.IsNullOrEmpty(_gameSyncMessage))
        {
            Widgets.Gap(Theme.Space.Sm);
            Widgets.TextWrapped(_gameSyncMessage, Theme.Dim, Theme.Caption);
        }
        Widgets.Gap(Theme.Space.Lg);

        Widgets.TwoColumn("game", 0.5f,
            left: () =>
            {
                Widgets.SectionHeader("On the server");
                if (_gameStateError is not null) Widgets.Text(_gameStateError, Theme.WatchInk, Theme.Caption);
                else if (_gameState is null) Widgets.Text("Loading...", Theme.Dim, Theme.Caption);
                else
                {
                    var head = _gameState.Head;
                    InfoRow("Latest", head is null ? "Nothing uploaded yet" : FormatAgo(DateTime.UtcNow - AsUtc(head.CreatedAt)));
                    if (head is not null)
                    {
                        InfoRow("Saved by", head.MachineName);
                        InfoRow("Size", FormatBytes(head.Size));
                    }
                    InfoRow("Stored", FormatBytes(_gameState.TotalStorageBytes));
                    var holder = _gameState.Lease?.HolderMachineName;
                    InfoRow("Checked out", holder is null ? "by nobody" : holder == _config.MachineName ? "by this Deck" : "by " + holder,
                        colour: holder is not null && holder != _config.MachineName ? Theme.Watch : null);
                }
            },
            right: () =>
            {
                Widgets.SectionHeader("On this device");
                InfoRow(g.ExtraPaths.Count == 0 ? "Folder" : "Main", missing ? "not set" : g.SaveDirectory, mono: true);
                foreach (var p in g.ExtraPaths)
                    InfoRow(p.Label ?? p.Key, p.Directory ?? "kept as a copy (not on this Deck)", mono: p.IsMapped,
                        colour: p.IsMapped ? null : Theme.Dim);
                InfoRow("Last push", g.LastPushAt is { } at ? FormatAgo(DateTime.UtcNow - at) : "none yet");
                InfoRow("Sent", g.LastPushBytes is { } b ? FormatBytes(b) : "-");
                InfoRow("Steam app", g.ResolveSteamAppId() ?? "-", mono: true);
            },
            // Fixed: the default fills the pane, which pushed the versions below it out of sight.
            height: 190f + 24f * g.ExtraPaths.Count);

        if (_suggestions.Count > 0)
        {
            Widgets.Gap(Theme.Space.Lg);
            Widgets.SectionHeader("Also found");
            Widgets.TextWrapped("The save database lists these folders for this game too, and they exist on this Deck. " +
                                "It cannot tell saves from settings, so nothing syncs until you add it.", Theme.Dim, Theme.Caption);
            var acting = _suggestionActionTask is { IsCompleted: false };
            foreach (var s in _suggestions)
            {
                ImGui.PushID(s.Path);   // every row has the same two labels
                Widgets.Text(s.Path, Theme.Fg, Theme.Caption);
                if (Widgets.PillButton("Add", Widgets.ButtonKind.Secondary, Icons.Check, enabled: !acting && Connected))
                    _suggestionActionTask = AddSuggestedFolderAsync(s);
                ImGui.SameLine(0, Theme.Space.Sm);
                if (Widgets.PillButton("Don't sync", Widgets.ButtonKind.Ghost, enabled: !acting))
                    _suggestionActionTask = IgnoreSuggestedFolderAsync(s);
                ImGui.PopID();
            }
        }

        Widgets.Gap(Theme.Space.Lg);
        Widgets.SectionHeader("Versions on the server");
        if (_gameVersions is null) Widgets.Text(_gameVersionsTask is null ? "Not available." : "Loading...", Theme.Dim, Theme.Caption);
        else if (_gameVersions.Count == 0) Widgets.Text("Nothing has been uploaded for this game yet.", Theme.Dim, Theme.Caption);
        else
        {
            foreach (var v in _gameVersions.Take(6))
            {
                var isHead = _gameState?.Head?.Id == v.Id;
                Widgets.Text(
                    $"{FormatAgo(DateTime.UtcNow - AsUtc(v.CreatedAt))}  ·  {v.MachineName}  ·  {FormatBytes(v.Size)}{(isHead ? "  ·  latest" : "")}",
                    isHead ? Theme.Safe : Theme.Dim, Theme.Caption);
            }
            if (_gameVersions.Count > 6)
                Widgets.Text($"and {_gameVersions.Count - 6} older", Theme.Faint, Theme.Caption);
        }
    }

    /// <summary>Server timestamps carry no zone but are UTC; a bare DateTime would be read as local.</summary>
    private static DateTime AsUtc(DateTime t) => t.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : t.ToUniversalTime();

    private void StartGameSync(TrackedGame g, string mode)
    {
        _gameSyncMessage = null;
        _gameSyncTask = GameSyncAsync(g.GameId, mode);
    }

    /// <summary>POST /api/games/{id}/sync — one game, never forced. A second press on the same game is a 409
    /// with a sentence, which is shown as it is.</summary>
    private async Task<string> GameSyncAsync(Guid id, string mode)
    {
        using var response = await DaemonClient().PostAsJsonAsync($"api/games/{id}/sync", new GameSyncRequest(mode));
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SyncNowResponse>())?.Message ?? "Done.";
        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        return err?.Error ?? $"The agent answered {(int)response.StatusCode}.";
    }

    private async Task<string?> AddSuggestedFolderAsync(FolderSuggestionDto s)
    {
        using var response = await DaemonClient().PostAsJsonAsync($"api/games/{s.GameId}/paths",
            new AddFolderRequest(s.Path, s.Key, FreeKey: true));
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<AddFolderResponse>() is { Joined: true } joined
                ? $"Another device had already added {s.Path} as \"{joined.Key}\". This Deck now syncs it too."
                : $"Now syncing {s.Path} on every device.";
        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        // A flagged folder or files on both sides are asked in the folder browser: the folder exists for the
        // game by then, so its own "Choose" button on this page takes it from here.
        return err?.Error ?? $"The agent answered {(int)response.StatusCode}.";
    }

    private async Task<string?> IgnoreSuggestedFolderAsync(FolderSuggestionDto s)
    {
        using var response = await DaemonClient().PostAsJsonAsync("api/folder-suggestions/answer",
            new FolderSuggestionAnswerRequest(s.GameId, Ignore: new[] { s.Path }));
        return response.IsSuccessStatusCode ? "SaveLocker won't suggest that folder again." : "Could not save that answer.";
    }

    private void EnterSetFolderForGame(TrackedGame g, string key = SaveRoot.PrimaryKey)
    {
        _folderGame = g;
        _folderKey = key;
        _folderFlag = null;
        _folderError = null;
        var current = key == SaveRoot.PrimaryKey
            ? g.SaveDirectory
            : g.ExtraPaths.FirstOrDefault(p => p.Key == key)?.Directory ?? g.SaveDirectory;
        _browsePath = !string.IsNullOrEmpty(current) && Directory.Exists(current) ? current : "";
        _lastListedPath = null;
        _screen = Screen.SetFolder;
    }

    /// <summary>Ask the daemon to change a game's folder (it validates: the hard refusals are absolute, the
    /// heuristic warnings come back as a question the caller must answer with confirm).</summary>
    private void ApplyGameFolder(TrackedGame g, string path, bool confirm, string? keep = null)
    {
        _folderError = null;
        _folderTask = SetFolderAsync(g.GameId, path, confirm, _folderKey, keep);
    }

    private async Task<(bool Ok, string? Error, string? Ask, string? Path, bool Choice)> SetFolderAsync(
        Guid id, string path, bool confirm, string key, string? keep)
    {
        using var response = await DaemonClient().PostAsJsonAsync($"api/games/{id}/folder",
            new FolderRequest(path, confirm, key == SaveRoot.PrimaryKey ? null : key, keep));
        if (response.IsSuccessStatusCode) return (true, null, null, null, false);
        var refusal = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        var message = refusal?.Error ?? "The agent refused that folder.";
        // Only the heuristic warnings are confirmable, and the agent says so in a field of its own, so a
        // hard refusal can never be clicked past — whatever either message happens to say.
        if (refusal?.NeedsConfirm == true) return (false, null, message.Replace(ErrorResponse.ConfirmHint, ""), path, false);
        if (refusal?.NeedsChoice == true && keep is null) return (false, null, message, path, true);
        return (false, message, null, null, false);
    }

    // ── Activity ─────────────────────────────────────────────────────────────────────────────

    private IReadOnlyList<OfflineQueue.Entry> _queue = Array.Empty<OfflineQueue.Entry>();
    private long _queueReadAt;

    private void DrawActivityScreen()
    {
        var now = Environment.TickCount64;
        if (now - _activityReadAt > ActivityPollMs || _activityReadAt == 0)
        {
            (_activityCurrent, _activityRecent) = _activityStore.Read();
            _activityReadAt = now;
        }
        // The queue is a file the daemon and the launch wrapper both write; a few seconds stale is fine.
        if (now - _queueReadAt > 3000 || _queueReadAt == 0)
        {
            _queue = OfflineQueue.For(_config).GetAll().OrderBy(e => e.QueuedAt).ToList();
            _queueReadAt = now;
        }

        Widgets.Text("Activity", Theme.Fg, Theme.Title);
        Widgets.Text($"{_activityRecent.Count} recent events · "
            + (_queue.Count == 0 ? "nothing waiting to upload" : $"{_queue.Count} waiting to upload"), Theme.Dim, Theme.Caption);
        Widgets.Gap(Theme.Space.Sm);

        bool syncing = _syncNowTask is { IsCompleted: false };
        if (Widgets.PillButton(syncing ? "Syncing..." : "Sync now", Widgets.ButtonKind.Secondary, Icons.Sync,
                enabled: !syncing && Connected))
            StartSyncNow();
        Widgets.Gap(Theme.Space.Md);

        Widgets.SectionHeader("Offline queue");
        if (_queue.Count == 0)
            Widgets.TextWrapped(
                "Nothing is waiting. If the server is unreachable when a save is pushed, it is kept here and "
                + "sent as soon as the connection comes back.", Theme.Dim, Theme.Caption);
        else
            foreach (var e in _queue)
            {
                var sub = $"queued {FormatAgo(DateTime.UtcNow - e.QueuedAt.UtcDateTime)} · {e.RetryCount} attempt{(e.RetryCount == 1 ? "" : "s")}"
                          + (e.Force ? " · forced" : "");
                Widgets.Text(e.GameName, Theme.Fg, Theme.BodyStrong);
                Widgets.Text(sub, Theme.Dim, Theme.Caption);
                Widgets.Gap(Theme.Space.Xs);
            }
        Widgets.Gap(Theme.Space.Lg);

        Widgets.SectionHeader("Everything the agent did");
        if (_activityRecent.Count == 0)
            Widgets.Text("No activity yet. Pushes, pulls and warnings show up here as they happen.", Theme.Dim, Theme.Caption);
        foreach (var e in _activityRecent)
            Widgets.Text($"{e.TimestampUtc.ToLocalTime():HH:mm:ss}  {e.Message}",
                System.Text.RegularExpressions.Regex.IsMatch(e.Message, "conflict|refused|failed|unreachable|blocked|error", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    ? Theme.WatchInk : Theme.Dim,
                Theme.Caption);
        Widgets.Gap(Theme.Space.Md);
        Widgets.Text("agent.log is at " + AgentLogger.LogPath, Theme.Faint, Theme.Caption);
    }

    // ── Battery ──────────────────────────────────────────────────────────────────────────────

    private int? _battery;
    private long _batteryReadAt;

    /// <summary>The first battery's charge, or null when there is none (a docked desktop, a test box).
    /// Read every 30 s, not per frame: it is a sysfs file, but the header draws 60 times a second.
    /// <c>SAVELOCKER_UI_FAKE_BATTERY</c> stands in for the file so a screenshot can be taken off a Deck.</summary>
    private int? ReadBattery()
    {
        var now = Environment.TickCount64;
        if (_batteryReadAt != 0 && now - _batteryReadAt < 30_000) return _battery;
        _batteryReadAt = now;
        _battery = null;
        try
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("SAVELOCKER_UI_FAKE_BATTERY"), out var fake))
                return _battery = Math.Clamp(fake, 0, 100);
            const string root = "/sys/class/power_supply";
            if (!Directory.Exists(root)) return null;
            foreach (var dir in Directory.EnumerateDirectories(root, "BAT*").OrderBy(d => d))
            {
                var file = Path.Combine(dir, "capacity");
                if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var pct))
                    return _battery = Math.Clamp(pct, 0, 100);
            }
        }
        catch { /* no battery reading is an answer, not an error */ }
        return null;
    }
}
