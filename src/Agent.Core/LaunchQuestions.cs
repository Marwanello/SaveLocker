using System.Text.Json;

namespace SaveLocker.Agent;

/// <summary>One answer a launch question offers.</summary>
public sealed record LaunchChoice(string Id, string Label, bool Primary = false);

/// <summary>
/// A question the pre-launch gate asks before a game starts (tasks/emulator-saves Phase 5c): which Xenia profile to start
/// as, and later the DLC check. <see cref="DefaultChoice"/> is <b>already applied</b> when the gate answers
/// <see cref="LaunchDecision.Ask"/>, so a client that does not know the question still launches correctly; one that shows
/// it posts any other choice to <c>POST /api/games/{id}/launch-answer</c> before launching, and launches on its own after
/// <see cref="TimeoutSeconds"/>.
/// </summary>
public sealed record LaunchQuestion(
    string Id,
    string Kind,
    string Title,
    string Text,
    IReadOnlyList<LaunchChoice> Choices,
    string DefaultChoice,
    int TimeoutSeconds,
    bool CanRemember);

/// <summary>A client's answer: <paramref name="Remember"/> asks to stop asking for this game.</summary>
public sealed record LaunchAnswerRequest(string QuestionId, string Choice, bool Remember = false);

/// <summary>
/// The questions asked at launch and their answers, on disk beside the config: on a Deck, Decky asks through the daemon
/// and the launch then runs through the Linux wrapper, a separate process that must see the answer, or it would undo
/// “Start as Player1” by applying the default again. An answer counts for <see cref="AnswerLife"/> — long enough to cover
/// that relaunch, short enough that the next session asks again.
/// </summary>
public sealed class LaunchQuestionStore
{
    public sealed class Entry
    {
        public string Id { get; set; } = "";
        public Guid GameId { get; set; }
        public string Kind { get; set; } = "";
        public DateTimeOffset AskedAt { get; set; }
        /// <summary>Each config file the default changed, and what it said before — what “keep” puts back.</summary>
        public Dictionary<string, string> Before { get; set; } = new();
        public string Applied { get; set; } = "";
        public string? Choice { get; set; }
        public DateTimeOffset? AnsweredAt { get; set; }
        /// <summary>A launch check already followed this answer; the next one asks afresh.</summary>
        public bool Used { get; set; }
    }

    public static readonly TimeSpan QuestionLife = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AnswerLife = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly string _stateDir;

    public static LaunchQuestionStore For(AgentConfig config) => new(Path.Combine(config.StateDir, "launch-questions.json"));

    public LaunchQuestionStore(string path)
    {
        _path = path;
        _stateDir = Path.GetDirectoryName(Path.GetFullPath(_path))!;
    }

    public void Add(Entry entry) => Mutate(list =>
    {
        list.RemoveAll(e => e.GameId == entry.GameId && e.Kind == entry.Kind && e.Choice is null);
        list.Add(entry);
    });

    public Entry? Find(string id) => ReadDisk().FirstOrDefault(e => e.Id == id && Live(e));

    /// <summary>Records the answer; false when the question is unknown, expired or already answered.</summary>
    public bool Answer(string id, string choice)
    {
        var ok = false;
        Mutate(list =>
        {
            var e = list.FirstOrDefault(x => x.Id == id && x.Choice is null);
            if (e is null) return;
            e.Choice = choice;
            e.AnsweredAt = DateTimeOffset.UtcNow;
            ok = true;
        });
        return ok;
    }

    /// <summary>
    /// The answer given for this game and kind within <see cref="AnswerLife"/> that no launch check has followed yet, if
    /// any — and marks it followed. It is for the check that comes right after the question within the same launch (on a
    /// Deck, Decky asks and then the Linux wrapper checks again); a later launch is asked afresh.
    /// </summary>
    public Entry? TakeRecentAnswer(Guid gameId, string kind)
    {
        Entry? taken = null;
        Mutate(list =>
        {
            taken = list
                .Where(e => e.GameId == gameId && e.Kind == kind && !e.Used && e.AnsweredAt > DateTimeOffset.UtcNow - AnswerLife)
                .OrderByDescending(e => e.AnsweredAt)
                .FirstOrDefault();
            if (taken is not null) taken.Used = true;
        });
        return taken;
    }

    private static bool Live(Entry e) =>
        e.AskedAt > DateTimeOffset.UtcNow - QuestionLife || e.AnsweredAt > DateTimeOffset.UtcNow - AnswerLife;

    private void Mutate(Action<List<Entry>> change)
    {
        try
        {
            using var guard = AgentStateLock.Acquire("launch-questions", _stateDir);
            var list = ReadDisk();
            list.RemoveAll(e => !Live(e));
            change(list);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOpts), restrictPermissions: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Best effort, like the lease warnings: a question that could not be stored only means the next launch
            // asks again, never that a launch is held up.
        }
    }

    private List<Entry> ReadDisk()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path), JsonOpts) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
}
