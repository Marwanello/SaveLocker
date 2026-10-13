using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>What to do when Xenia is set to start as another profile than the one a game's saves are under.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProfileAtLaunch>))]
public enum ProfileAtLaunch
{
    /// <summary>Switch to the game's profile, and let a client that can ask offer to keep the other one.</summary>
    Ask,
    /// <summary>Switch to the game's profile without asking.</summary>
    Switch,
    /// <summary>Never touch Xenia's config.</summary>
    Leave
}

/// <summary>
/// The profile checked at launch (tasks/emulator-saves Phase 5c, option F). A Xenia game's saves are under one profile —
/// its save folder is <c>content/&lt;XUID&gt;</c> — and Xenia only shows them while that profile is signed in, which its
/// config decides at boot (<see cref="XeniaConfig"/>). Before a launch, the configs are set to the game's profile; where a
/// client can ask (Decky, Playnite), it is offered to start as the other profile instead.
/// </summary>
public static partial class XeniaLaunchProfile
{
    public const string Kind = "xenia-profile";
    public const string UseGameProfile = "profile", KeepCurrent = "keep";

    [GeneratedRegex("^([0-9A-F]{8})/00000001/\\*\\*$", RegexOptions.IgnoreCase)]
    private static partial Regex SaveScope();

    /// <summary>The XUID and title ID a tracked Xenia game's saves are under, or null for any other game — including
    /// Xenia's layouts from before profiles, where who signs in does not move the saves.</summary>
    public static (string Xuid, string TitleId, string Content)? GameProfile(TrackedGame game)
    {
        if (string.IsNullOrEmpty(game.SaveDirectory)) return null;
        var titleId = game.IncludeGlobs.Select(g => SaveScope().Match(g.Replace('\\', '/'))).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        if (titleId is null) return null;
        var dir = EmuDeckRoots.RealPath(Path.TrimEndingDirectorySeparator(game.SaveDirectory));
        var xuid = XeniaSaves.Xuid(Path.GetFileName(dir));
        var content = Path.GetDirectoryName(dir);
        return xuid is null || content is null ? null : (xuid, titleId.ToUpperInvariant(), content);
    }

    public static ProfileAtLaunch PolicyFor(AgentConfig config, TrackedGame game) =>
        game.XeniaProfileAtLaunch ?? config.XeniaProfileAtLaunch ?? ProfileAtLaunch.Ask;

    /// <summary>
    /// Sets Xenia to start as the game's profile, or asks. Null when there is nothing to do: not a Xenia game under a
    /// profile, no config found, already right, the policy is Leave, or Xenia is running (its config is only read at boot,
    /// and an open Xenia would write its own back). <paramref name="canAsk"/> is false for the Linux wrapper, which has
    /// nowhere to show a question.
    /// </summary>
    public static LaunchGateResult? Check(AgentConfig config, TrackedGame game, LaunchQuestionStore store, bool canAsk,
        Action<string> log, Func<bool>? xeniaRunning = null, Action<LaunchProfileReport>? report = null)
    {
        if (GameProfile(game) is not { } profile) return null;
        var configs = XeniaConfig.For(profile.Content, game.Name);
        if (configs.Count == 0) return null;
        void Report(string? configured, string action) => report?.Invoke(new LaunchProfileReport(profile.Xuid,
            XeniaProfile.Gamertag(profile.Content, profile.Xuid), string.IsNullOrEmpty(configured) ? null : configured,
            string.IsNullOrEmpty(configured) ? null : XeniaProfile.Gamertag(profile.Content, configured), action));

        var target = profile.Xuid;
        // Only a check that cannot ask follows an earlier answer: on a Deck, Decky asked a moment ago and this is the same
        // launch reaching the Linux wrapper. A client that can ask is starting a launch of its own, so it asks afresh.
        if (!canAsk && store.TakeRecentAnswer(game.GameId, Kind) is { } answered)
        {
            if (answered.Choice == KeepCurrent) return null;
            target = answered.Applied;
        }

        var current = configs.ToDictionary(c => c, c => XeniaConfig.Read(c) ?? "");
        var before = current.FirstOrDefault(kv => kv.Value != target).Value ?? "";
        if (current.Values.All(v => v == target))
        {
            Report(target, "already");
            return null;
        }
        var policy = PolicyFor(config, game);
        if (policy == ProfileAtLaunch.Leave)
        {
            Report(before, "left");
            return null;
        }
        if ((xeniaRunning ?? XeniaRunning)())
        {
            log($"[{game.Name}] Xenia is already running, so its profile was left as it is.");
            Report(before, "running");
            return null;
        }

        foreach (var c in configs.Where(c => current[c] != target)) XeniaConfig.Write(c, target);
        var gameTag = Name(profile.Content, target);
        var beforeTag = before.Length == 0 ? null : Name(profile.Content, before);
        log($"[{game.Name}] Xenia set to start as {gameTag} (was {beforeTag ?? "no profile"}).");

        Report(before, policy == ProfileAtLaunch.Switch || !canAsk ? "switched" : "asked");
        if (policy == ProfileAtLaunch.Switch || !canAsk)
            return new LaunchGateResult(LaunchDecision.Proceed, $"Xenia set to start as {gameTag}.");

        var id = Guid.NewGuid().ToString("N");
        store.Add(new LaunchQuestionStore.Entry
        {
            Id = id, GameId = game.GameId, Kind = Kind, AskedAt = DateTimeOffset.UtcNow, Applied = target,
            Before = current.Where(kv => kv.Value != target).ToDictionary(kv => kv.Key, kv => kv.Value),
        });
        return new LaunchGateResult(LaunchDecision.Ask, $"Xenia set to start as {gameTag}.", Question: new LaunchQuestion(
            id, Kind,
            beforeTag is null ? "Xenia wasn’t set to sign in anyone" : $"Xenia would start as {beforeTag}",
            $"{game.Name}’s saves and achievements are under {gameTag}. " +
            (beforeTag is null ? "Without a profile the game can’t load them." : $"As {beforeTag} the game starts without them."),
            [new LaunchChoice(UseGameProfile, $"Start as {gameTag}", Primary: true), new LaunchChoice(KeepCurrent, beforeTag is null ? "Start without a profile" : $"Start as {beforeTag}")],
            UseGameProfile, TimeoutSeconds: 30, CanRemember: true));
    }

    /// <summary>
    /// Applies a client's answer. “keep” puts back what each config said before — only where it still says what this
    /// launch wrote, so a change made since is never undone. Remember stops asking for this game.
    /// </summary>
    public static bool Answer(AgentConfig config, TrackedGame game, LaunchQuestionStore store, LaunchAnswerRequest answer, Action<string> log,
        Action<LaunchProfileReport>? report = null)
    {
        if (store.Find(answer.QuestionId) is not { } q || q.GameId != game.GameId || q.Kind != Kind) return false;
        if (answer.Choice is not (UseGameProfile or KeepCurrent)) return false;
        if (!store.Answer(q.Id, answer.Choice)) return false;

        if (answer.Choice == KeepCurrent)
        {
            foreach (var (path, value) in q.Before)
                if (File.Exists(path) && XeniaConfig.Read(path) == q.Applied) XeniaConfig.Write(path, value);
            if (GameProfile(game) is { } profile && q.Before.Values.FirstOrDefault() is { } kept)
                report?.Invoke(new LaunchProfileReport(profile.Xuid, XeniaProfile.Gamertag(profile.Content, profile.Xuid),
                    kept.Length == 0 ? null : kept, kept.Length == 0 ? null : XeniaProfile.Gamertag(profile.Content, kept), "kept"));
        }
        if (answer.Remember)
        {
            var value = answer.Choice == KeepCurrent ? ProfileAtLaunch.Leave : ProfileAtLaunch.Switch;
            config.SaveGameProfileAtLaunch(game.GameId, value);
            game.XeniaProfileAtLaunch = value;
        }
        log($"[{game.Name}] start as {(answer.Choice == KeepCurrent ? "the other profile" : "the game’s profile")}" +
            (answer.Remember ? ", and don’t ask again for this game." : "."));
        return true;
    }

    private static string Name(string content, string xuid) => XeniaProfile.Gamertag(content, xuid) ?? xuid;

    /// <summary>Xenia under any of its names, natively or under Wine/Proton (where the process keeps its .exe).</summary>
    public static bool XeniaRunning()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (IsXeniaProcessName(p.ProcessName)) return true;
            }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        return false;
    }

    private static readonly string[] XeniaNames = ["xenia", "xenia_canary", "xenia_edge", "xenia-canary"];

    /// <summary>
    /// Whether a process name is Xenia's. Linux keeps only 15 characters of a name (<c>/proc/&lt;pid&gt;/comm</c>), and
    /// Wine names a process after its .exe, so under Proton Xenia Canary is <c>xenia_canary.ex</c> — .NET reads the full
    /// name from the command line only when that starts with the short one, and Wine's is a Windows path.
    /// </summary>
    public static bool IsXeniaProcessName(string processName)
    {
        var name = processName.ToLowerInvariant();
        var bare = name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
        if (XeniaNames.Contains(bare)) return true;
        return name.Length == 15 && XeniaNames.Any(n => (n + ".exe").StartsWith(name, StringComparison.Ordinal));
    }
}
