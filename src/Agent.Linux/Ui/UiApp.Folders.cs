using System.Net.Http.Json;
using System.Numerics;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// "More save folders found" (tasks/multiple-save-paths plan §8) — the Deck's counterpart of agent-ui's
/// FolderSuggestionsModal, as a screen since this UI has no modals: once per start, games tracked before an
/// agent could sync several folders are offered the manifest's other folders one game at a time, every one
/// ticked. Unticked folders and a skipped game's folders are deferred: they stay under "Also found" on the
/// game's page and are not asked about again. Also the "Also found" ticks of Add game's candidate rows.
/// </summary>
sealed partial class UiApp
{
    private sealed record PromptGame(Guid GameId, string GameName, List<FolderSuggestionDto> Items);

    /// <summary>False under a <c>--screen</c> capture of another screen, which must not be taken over.</summary>
    private bool _folderPromptAllowed = true;
    private Task<List<FolderSuggestionDto>?>? _folderPromptFetch;
    private bool _folderPromptAsked;
    private int _folderPromptAttempts;
    private long _folderPromptRetryAt;
    private const int FolderPromptMaxAttempts = 4;
    private const long FolderPromptRetryMs = 15000;

    private List<PromptGame> _promptGames = new();
    private int _promptIndex;
    private readonly HashSet<string> _promptOff = new();
    private Task<string?>? _promptTask;
    private bool _promptSkipAll;
    private string? _promptProblem;

    // Add game: the candidate's "Also found" folders the user unticked, by candidate index.
    private readonly Dictionary<int, HashSet<string>> _alsoOff = new();

    /// <summary>Every frame: ask the daemon once per start (retrying while it is still coming up), and open the
    /// prompt over Overview when a game has undecided folders.</summary>
    private void PollFolderPrompt()
    {
        if (!_folderPromptAsked && Connected && _folderPromptFetch is null
            && _folderPromptAttempts < FolderPromptMaxAttempts && Environment.TickCount64 >= _folderPromptRetryAt)
        {
            _folderPromptAttempts++;
            _folderPromptFetch = DaemonGet<List<FolderSuggestionDto>>("api/folder-suggestions", 30);
        }

        if (_folderPromptFetch is { IsCompleted: true } fetch)
        {
            _folderPromptFetch = null;
            if (!fetch.IsCompletedSuccessfully)
            {
                _folderPromptRetryAt = Environment.TickCount64 + FolderPromptRetryMs;
                return;
            }
            _folderPromptAsked = true;
            _promptGames = (fetch.Result ?? new())
                .Where(s => !s.Deferred)
                .GroupBy(s => s.GameId)
                .Select(g => new PromptGame(g.Key, g.First().GameName, g.ToList()))
                .ToList();
            _promptIndex = 0;
            _promptOff.Clear();
            _promptProblem = null;
            if (_promptGames.Count > 0 && _folderPromptAllowed && _screen is Screen.Status or Screen.Folders)
            {
                Go(Screen.Folders);
                _pendingCrossFrames = PendingCrossFrames;   // land on the first tick, not the rail
            }
        }

        if (_promptTask is { IsCompleted: true } done)
        {
            _promptTask = null;
            _diskReadAt = 0;                   // the daemon may just have written new folders
            _promptProblem = done.IsCompletedSuccessfully ? done.Result : done.Exception?.GetBaseException().Message;
            if (_promptSkipAll) { _promptSkipAll = false; _promptIndex = _promptGames.Count - 1; }
            if (_promptProblem is null) NextPromptGame();
        }
    }

    private void NextPromptGame()
    {
        _promptOff.Clear();
        _promptProblem = null;
        _promptIndex++;
        if (_promptIndex < _promptGames.Count) { _bestContentId = 0; _pendingCrossFrames = PendingCrossFrames; return; }
        _promptGames = new();
        Go(Screen.Status);
    }

    private void DrawFolderPrompt()
    {
        if (_promptIndex >= _promptGames.Count)
        {
            Widgets.Text("No save folders are waiting on you.", Theme.Dim);
            return;
        }
        var game = _promptGames[_promptIndex];
        var busy = _promptTask is { IsCompleted: false };
        var ticked = game.Items.Count(s => !_promptOff.Contains(s.Path));

        Widgets.Text("More save folders found", Theme.Fg, Theme.Title);
        if (_promptGames.Count > 1)
            Widgets.Text($"Game {_promptIndex + 1} of {_promptGames.Count}", Theme.WatchInk, Theme.Caption);
        Widgets.Gap(Theme.Space.Md);

        Widgets.SectionHeader(game.GameName);
        Widgets.TextWrapped($"The save database lists more folders for {game.GameName} that exist on this Deck, and " +
                            "SaveLocker doesn't sync them yet. It cannot tell saves from settings, so check them: ticked " +
                            "folders sync on every device.", Theme.Dim, Theme.Caption);
        Widgets.Gap(Theme.Space.Sm);

        foreach (var s in game.Items)
        {
            ImGui.PushID(s.Path);
            var on = !_promptOff.Contains(s.Path);
            if (Widgets.CheckRow("on", ref on, enabled: !busy && _promptProblem is null))
            {
                if (on) _promptOff.Remove(s.Path); else _promptOff.Add(s.Path);
            }
            ImGui.SameLine(0, Theme.Space.Md);
            ImGui.AlignTextToFramePadding();
            Widgets.Text(s.Path, on ? Theme.Fg : Theme.Dim, Theme.Caption);
            ImGui.PopID();
        }
        Widgets.Gap(Theme.Space.Md);

        if (_promptProblem is not null)
        {
            Widgets.TextWrapped(_promptProblem, Theme.WatchInk, Theme.Caption);
            Widgets.Gap(Theme.Space.Sm);
            if (Widgets.PillButton("Continue", Widgets.ButtonKind.Primary)) NextPromptGame();
        }
        else
        {
            var label = ticked == 0 ? "Add selected" : $"Add {ticked} folder{(ticked == 1 ? "" : "s")}";
            if (Widgets.PillButton(busy ? "Adding..." : label, Widgets.ButtonKind.Primary, Icons.Check,
                    enabled: !busy && ticked > 0 && Connected))
                _promptTask = AddPromptFoldersAsync(game, game.Items.Where(s => !_promptOff.Contains(s.Path)).ToList());
            ImGui.SameLine(0, Theme.Space.Sm);
            if (Widgets.PillButton("Skip for now", Widgets.ButtonKind.Secondary, enabled: !busy))
                _promptTask = DeferAsync(game, game.Items);
            if (_promptGames.Count - _promptIndex > 1)
            {
                ImGui.SameLine(0, Theme.Space.Sm);
                if (Widgets.PillButton("Skip all", Widgets.ButtonKind.Ghost, enabled: !busy))
                {
                    _promptSkipAll = true;
                    _promptTask = SkipAllAsync(_promptGames.Skip(_promptIndex).ToList());
                }
            }
        }
        Widgets.Gap(Theme.Space.Sm);
        Widgets.TextWrapped("Skipped folders stay on each game's page under Also found, to add later.",
            Theme.Faint, Theme.Caption);
    }

    /// <summary>Adds the ticked folders, defers the rest. A folder the daemon wants a second look at (a flagged
    /// folder, files on both sides) is not decided here — it is deferred too, and the game's page asks.</summary>
    private async Task<string?> AddPromptFoldersAsync(PromptGame game, List<FolderSuggestionDto> picked)
    {
        var failed = new List<string>();
        var left = game.Items.Where(s => !picked.Contains(s)).Select(s => s.Path).ToList();
        foreach (var s in picked)
        {
            using var response = await DaemonClient().PostAsJsonAsync($"api/games/{s.GameId}/paths",
                new AddFolderRequest(s.Path, s.Key, FreeKey: true));
            if (response.IsSuccessStatusCode) continue;
            left.Add(s.Path);
            var err = await ReadError(response);
            failed.Add($"{s.Path}: {err}");
        }
        await PostDefer(game.GameId, left);
        return failed.Count == 0 ? null
            : $"Not added - finish {(failed.Count == 1 ? "it" : "them")} on the game's page: {string.Join("  |  ", failed)}";
    }

    private async Task<string?> DeferAsync(PromptGame game, List<FolderSuggestionDto> items)
    {
        await PostDefer(game.GameId, items.Select(s => s.Path).ToList());
        return null;
    }

    private async Task<string?> SkipAllAsync(List<PromptGame> games)
    {
        foreach (var g in games) await PostDefer(g.GameId, g.Items.Select(s => s.Path).ToList());
        return null;
    }

    private async Task PostDefer(Guid gameId, List<string> paths)
    {
        if (paths.Count == 0) return;
        try
        {
            using var _ = await DaemonClient().PostAsJsonAsync("api/folder-suggestions/answer",
                new FolderSuggestionAnswerRequest(gameId, Defer: paths.ToArray()));
        }
        catch { /* an unanswered folder is only asked again next start */ }
    }

    private static async Task<string> ReadError(HttpResponseMessage response)
    {
        try { return (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error ?? $"The agent answered {(int)response.StatusCode}."; }
        catch { return $"The agent answered {(int)response.StatusCode}."; }
    }

    // ── Add game: "Also found" ticks on a candidate row ─────────────────────────────────────────

    /// <summary>Under a candidate: the manifest's other folders for it that exist here, ticked unless the user
    /// unticks them (agent-ui's AddGamesView does the same).</summary>
    private void DrawCandidateAlsoFound(int i, ScanCandidate c)
    {
        if (c.AlternateSaveDirs is not { Count: > 0 } also || string.IsNullOrEmpty(c.SuggestedSaveDir)) return;
        Widgets.Gap(Theme.Space.Xs);
        Widgets.Text("Also found - synced with it unless you untick", Theme.Dim, Theme.Caption);
        _alsoOff.TryGetValue(i, out var off);
        foreach (var a in also)
        {
            ImGui.PushID(a.Dir);
            var on = off is null || !off.Contains(a.Dir);
            if (Widgets.CheckRow("also", ref on))
            {
                if (!_alsoOff.TryGetValue(i, out off)) _alsoOff[i] = off = new HashSet<string>(StringComparer.Ordinal);
                if (on) off.Remove(a.Dir); else off.Add(a.Dir);
            }
            ImGui.SameLine(0, Theme.Space.Sm);
            ImGui.AlignTextToFramePadding();
            Widgets.Text(a.Dir, on ? Theme.Fg : Theme.Dim, Theme.Caption);
            ImGui.PopID();
        }
    }

    /// <summary>The ticked "Also found" folders of each selected candidate, as <see cref="Enroller.EnrollAsync"/>
    /// takes them.</summary>
    private Dictionary<int, string[]> AlsoSyncChoices() =>
        _selected
            .Where(i => i >= 0 && i < _candidates.Count && _candidates[i].AlternateSaveDirs is { Count: > 0 })
            .ToDictionary(i => i, i => _candidates[i].AlternateSaveDirs!
                .Select(a => a.Dir)
                .Where(d => !_alsoOff.TryGetValue(i, out var off) || !off.Contains(d))
                .ToArray());
}
