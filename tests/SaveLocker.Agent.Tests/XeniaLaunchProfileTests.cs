using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 5c, steps 2–3: the profile checked at launch. A Xenia game's saves are under the XUID its
/// save folder is named after; before it starts, Xenia's config is set to that profile, and a client that can ask
/// (Decky, Playnite) is offered to keep the other one. The default is applied before asking, so a client that ignores
/// the question still launches right.
/// </summary>
public sealed class XeniaLaunchProfileTests : IDisposable
{
    private const string Game = "E030000033894516", Player1 = "E03000004A1C77B2";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-xlaunch-" + Guid.NewGuid().ToString("N"));
    private readonly string _install, _content, _rootConfig;
    private readonly AgentConfig _config;
    private readonly LaunchQuestionStore _store;
    private readonly List<string> _log = [];
    private TrackedGame _game;

    public XeniaLaunchProfileTests()
    {
        _install = Path.Combine(_root, "Xenia Canary");
        _content = Path.Combine(_install, "content");
        _rootConfig = Path.Combine(_install, "xenia-canary.config.toml");
        Directory.CreateDirectory(Path.Combine(_content, Game, "4D5309C9", "00000001"));
        File.WriteAllText(Path.Combine(_install, "xenia_canary.exe"), "");
        File.WriteAllText(_rootConfig, Config(Player1));
        Profile(Game, "Driver");
        Profile(Player1, "Player1");

        _config = AgentConfig.Load(Path.Combine(_root, "state", "config.json"));
        _game = new TrackedGame
        {
            GameId = Guid.NewGuid(), Name = "Forza Horizon",
            SaveDirectory = Path.Combine(_content, Game), IncludeGlobs = [.. XeniaSaves.ScopeFor("4D5309C9")],
        };
        _config.MutateGames(list => list.Add(_game));
        _config.Save();
        _store = LaunchQuestionStore.For(_config);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Config(string xuid) =>
        $"[Profiles]\nlogged_profile_slot_0_xuid = \"{xuid}\"   \t# XUID of the profile to load on boot in slot 0\n";

    private void Profile(string xuid, string gamertag)
    {
        var dir = XeniaProfile.Folder(_content, xuid);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Account"), XeniaFormatsTests.AccountFor(gamertag));
    }

    private LaunchGateResult? Check(bool canAsk, bool running = false) =>
        XeniaLaunchProfile.Check(_config, _game, _store, canAsk, _log.Add, () => running);

    [Fact]
    public void A_client_that_can_ask_gets_the_question_with_the_games_profile_already_set()
    {
        var r = Check(canAsk: true);

        Assert.Equal(LaunchDecision.Ask, r!.Decision);
        Assert.Equal(Game, XeniaConfig.Read(_rootConfig));
        var q = r.Question!;
        Assert.Equal("Xenia would start as Player1", q.Title);
        Assert.Contains("under Driver", q.Text);
        Assert.Equal(XeniaLaunchProfile.UseGameProfile, q.DefaultChoice);
        Assert.Equal(["Start as Driver", "Start as Player1"], q.Choices.Select(c => c.Label));
    }

    [Fact]
    public void Keeping_the_other_profile_puts_it_back_and_the_wrappers_check_right_after_leaves_it()
    {
        var q = Check(canAsk: true)!.Question!;

        Assert.True(XeniaLaunchProfile.Answer(_config, _game, _store, new LaunchAnswerRequest(q.Id, XeniaLaunchProfile.KeepCurrent), _log.Add));
        Assert.Equal(Player1, XeniaConfig.Read(_rootConfig));

        // On a Deck the same launch then runs through the Linux wrapper, a separate process that cannot ask.
        Assert.Null(Check(canAsk: false));
        Assert.Equal(Player1, XeniaConfig.Read(_rootConfig));
        Assert.False(XeniaLaunchProfile.Answer(_config, _game, _store, new LaunchAnswerRequest(q.Id, XeniaLaunchProfile.UseGameProfile), _log.Add));
    }

    [Fact]
    public void Dont_ask_again_saves_the_games_own_policy()
    {
        var q = Check(canAsk: true)!.Question!;

        Assert.True(XeniaLaunchProfile.Answer(_config, _game, _store, new LaunchAnswerRequest(q.Id, XeniaLaunchProfile.UseGameProfile, Remember: true), _log.Add));

        Assert.Equal(ProfileAtLaunch.Switch, AgentConfig.Load(_config.ConfigPath).Games.Single().XeniaProfileAtLaunch);
        File.WriteAllText(_rootConfig, Config(Player1));
        var next = new LaunchQuestionStore(Path.Combine(_root, "other-questions.json"));
        Assert.Equal(LaunchDecision.Proceed, XeniaLaunchProfile.Check(_config, _game, next, true, _log.Add, () => false)!.Decision);
        Assert.Equal(Game, XeniaConfig.Read(_rootConfig));
    }

    [Fact]
    public void The_wrapper_switches_without_a_question()
    {
        var r = Check(canAsk: false);

        Assert.Equal(LaunchDecision.Proceed, r!.Decision);
        Assert.Null(r.Question);
        Assert.Equal(Game, XeniaConfig.Read(_rootConfig));
    }

    [Fact]
    public void Every_check_reports_what_it_did_for_the_consoles_table()
    {
        var reports = new List<SaveLocker.Shared.LaunchProfileReport>();
        LaunchGateResult? Run(bool canAsk, bool running = false) =>
            XeniaLaunchProfile.Check(_config, _game, _store, canAsk, _log.Add, () => running, reports.Add);

        _config.XeniaProfileAtLaunch = ProfileAtLaunch.Leave;
        Run(canAsk: true);
        _config.XeniaProfileAtLaunch = null;
        Run(canAsk: true, running: true);
        Run(canAsk: false);
        Run(canAsk: false);

        Assert.Equal(["left", "running", "switched", "already"], reports.Select(r => r.Action));
        Assert.All(reports, r => Assert.Equal(Game, r.GameXuid));
        Assert.Equal(Player1, reports[0].ConfiguredXuid);
        Assert.Equal(Game, reports[3].ConfiguredXuid);
    }

    [Fact]
    public void Leave_already_right_running_or_no_profile_change_nothing()
    {
        _config.XeniaProfileAtLaunch = ProfileAtLaunch.Leave;
        Assert.Null(Check(canAsk: true));
        Assert.Equal(Player1, XeniaConfig.Read(_rootConfig));

        _config.XeniaProfileAtLaunch = null;
        Assert.Null(Check(canAsk: true, running: true));
        Assert.Equal(Player1, XeniaConfig.Read(_rootConfig));

        File.WriteAllText(_rootConfig, Config(Game));
        Assert.Null(Check(canAsk: true));

        _game.SaveDirectory = _content; // the layout from before profiles: content/<title ID>
        _game.IncludeGlobs = [.. XeniaSaves.ScopeFor("4D5309C9")];
        File.WriteAllText(_rootConfig, Config(Player1));
        Assert.Null(Check(canAsk: true));
        Assert.Equal(Player1, XeniaConfig.Read(_rootConfig));
    }

    [Fact]
    public void Xenia_Managers_config_for_the_game_is_switched_too_and_put_back_with_the_root_one()
    {
        var own = Path.Combine(_install, "config", "Forza Horizon.config.toml");
        var other = Path.Combine(_install, "config", "Halo 3.config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, Config(Player1));
        File.WriteAllText(other, Config(Player1));

        var q = Check(canAsk: true)!.Question!;
        Assert.Equal(Game, XeniaConfig.Read(own));
        Assert.Equal(Player1, XeniaConfig.Read(other));

        XeniaLaunchProfile.Answer(_config, _game, _store, new LaunchAnswerRequest(q.Id, XeniaLaunchProfile.KeepCurrent), _log.Add);
        Assert.Equal(Player1, XeniaConfig.Read(own));
        Assert.Equal(Player1, XeniaConfig.Read(_rootConfig));
    }

    [Fact]
    public void An_unset_config_names_no_profile_and_an_unknown_answer_is_refused()
    {
        File.WriteAllText(_rootConfig, Config(""));

        var q = Check(canAsk: true)!.Question!;

        Assert.Equal("Xenia wasn’t set to sign in anyone", q.Title);
        Assert.Equal("Start without a profile", q.Choices[1].Label);
        Assert.False(XeniaLaunchProfile.Answer(_config, _game, _store, new LaunchAnswerRequest(q.Id, "something else"), _log.Add));
        Assert.False(XeniaLaunchProfile.Answer(_config, _game, _store, new LaunchAnswerRequest("no-such-question", XeniaLaunchProfile.KeepCurrent), _log.Add));
    }
}
