using System.Text;
using SaveLocker.Agent.Linux;
using SaveLocker.Agent.Linux.Art;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The test rig's two Steam shortcuts ("Conflict Game" and "SaveLocker Test") share one shortcuts.vdf, one
/// backup and one grid folder: removing either must leave the other — and the person's own shortcuts — intact.
/// </summary>
public sealed class SteamShortcutWriterTests : IDisposable
{
    // The trailing slash keeps Path.Combine from joining with '\' when the tests run on Windows.
    private const string Prefix = "/home/deck/savelocker-test/";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "savelocker-shortcut-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "userdata", "1", "config");
    private string Vdf => Path.Combine(Config, "shortcuts.vdf");
    private string Grid => Path.Combine(Config, "grid");
    private string Backup => Vdf + ".savelocker-backup";

    public SteamShortcutWriterTests() => Directory.CreateDirectory(Config);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static byte[] Layer(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "packaging"))) dir = dir.Parent;
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "src", "Agent.Linux", "Art", "layers", name));
    }

    /// <summary>A shortcuts.vdf holding one shortcut of the person's own, in the shape Steam writes.</summary>
    private byte[] WriteUsersOwnFile()
    {
        static byte[] S(string s) => Encoding.UTF8.GetBytes(s + "\0");
        var bytes = new byte[][]
        {
            [0x00], S("shortcuts"),
            [0x00], S("0"),
            [0x02], S("appid"), BitConverter.GetBytes(-123456),
            [0x01], S("AppName"), S("Their Game"),
            [0x01], S("Exe"), S("\"/usr/bin/their-game\""),
            [0x00], S("tags"), [0x08],
            [0x08],
            [0x08], [0x08],
        }.SelectMany(b => b).ToArray();
        File.WriteAllBytes(Vdf, bytes);
        return bytes;
    }

    private int AddUi()
    {
        var appId = SteamShortcutWriter.AddTo(Vdf, Prefix, false, SteamShortcutWriter.DeckUi, "--port 5177", "%command%");
        Assert.NotNull(appId);
        SteamShortcutWriter.PaintArt(Grid, unchecked((uint)appId.Value), "ember", "pixel", Layer);
        return appId.Value;
    }

    private void AddConflict() => Assert.NotNull(SteamShortcutWriter.AddTo(Vdf, Prefix, false, SteamShortcutWriter.Conflict, null, null));

    private string[] Art(int appId) =>
        SteamArt.ShortcutFiles(unchecked((uint)appId)).Select(f => Path.Combine(Grid, f.Name)).ToArray();

    [Fact]
    public void TheAppId_IsValvesCrc32OfExeAndName_TheNumberTheGridFilesAreNamedBy()
    {
        // zlib.crc32(b'"/home/deck/savelocker-test/savelocker" ui --port 5177SaveLocker Test') | 0x80000000
        Assert.Equal(-780779068, AddUi());
        Assert.True(File.Exists(Path.Combine(Grid, "3514188228p.png")));
    }

    [Fact]
    public void RemovingTheConflictEntry_LeavesTheUiEntrysArt()
    {
        WriteUsersOwnFile();
        var ui = AddUi();
        AddConflict();

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.Conflict));
        Assert.All(Art(ui), path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void RemovingTheUiEntry_DeletesItsArt_EvenWhenTheFileItCreatedGoesToo()
    {
        var ui = AddUi();   // no shortcuts.vdf before: SaveLocker creates it

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckUi));
        Assert.False(File.Exists(Vdf));
        Assert.All(Art(ui), path => Assert.False(File.Exists(path), path));
        Assert.False(Directory.Exists(Grid));
    }

    [Fact]
    public void RemovingAnEntryThatIsNotThere_WhileTheOtherIs_IsNotARefusal()
    {
        var original = WriteUsersOwnFile();
        AddConflict();

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckUi));
        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.Conflict));
        Assert.Equal(original, File.ReadAllBytes(Vdf));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void TheBackup_StaysThePreSaveLockerFile_UntilTheLastTestEntryGoes()
    {
        var original = WriteUsersOwnFile();
        AddUi();
        AddConflict();

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckUi));   // testenv up removes and re-adds it
        Assert.Equal(original, File.ReadAllBytes(Backup));
        AddUi();
        Assert.Equal(original, File.ReadAllBytes(Backup));

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.Conflict));
        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckUi));
        Assert.Equal(original, File.ReadAllBytes(Vdf));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void AFileSaveLockerCreated_IsDeleted_OnceBothEntriesAreGone()
    {
        AddUi();
        AddConflict();

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckUi));
        Assert.True(File.Exists(Vdf));
        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.Conflict));
        Assert.False(File.Exists(Vdf));
        Assert.False(File.Exists(Vdf + ".savelocker-created"));
        Assert.False(File.Exists(Backup));
    }

    /* ---- The shipped "SaveLocker" entry: inspection and in-place repair (controller-era Phase 6) ---- */

    /// <summary>A shortcut with the shipped name that points somewhere else — never ours to touch.</summary>
    private byte[] WriteForeignSaveLocker()
    {
        static byte[] S(string s) => Encoding.UTF8.GetBytes(s + "\0");
        var bytes = new byte[][]
        {
            [0x00], S("shortcuts"),
            [0x00], S("0"),
            [0x02], S("appid"), BitConverter.GetBytes(-42),
            [0x01], S("AppName"), S("SaveLocker"),
            [0x01], S("Exe"), S("\"/usr/bin/their-game\""),
            [0x00], S("tags"), [0x08],
            [0x08],
            [0x08], [0x08],
        }.SelectMany(b => b).ToArray();
        File.WriteAllBytes(Vdf, bytes);
        return bytes;
    }

    [Fact]
    public void Inspect_ReadsTheEntriesCommand()
    {
        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Missing,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out _));

        // The shipped name running the old SDL command: what a pre-controller-era Deck has.
        var legacy = new SteamShortcutWriter.Kind("SaveLocker", "ui");
        Assert.NotNull(SteamShortcutWriter.AddTo(Vdf, Prefix, false, legacy, null, null));
        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Stale,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out var exe));
        Assert.Contains("\" ui", exe);

        Assert.True(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));
        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Current,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out var repaired));
        Assert.Contains("\" open", repaired);
        // The repair is idempotent: nothing left to do on a current entry.
        Assert.False(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));
    }

    /// <summary>One of our entries, written by hand in a given name-key case and Exe/LaunchOptions shape.</summary>
    private byte[] WriteOurEntry(string nameKey, string exe, string launchOptions)
    {
        static byte[] S(string s) => Encoding.UTF8.GetBytes(s + "\0");
        var bytes = new byte[][]
        {
            [0x00], S("shortcuts"),
            [0x00], S("0"),
            [0x02], S("appid"), BitConverter.GetBytes(-4242),
            [0x01], S(nameKey), S("SaveLocker"),
            [0x01], S("Exe"), S(exe),
            [0x01], S("LaunchOptions"), S(launchOptions),
            [0x00], S("tags"), [0x08],
            [0x08],
            [0x08], [0x08],
        }.SelectMany(b => b).ToArray();
        File.WriteAllBytes(Vdf, bytes);
        return bytes;
    }

    [Fact]
    public void AnEntrySpelledAppname_IsStillFound_AndRepaired()
    {
        // Steam's own spelling, and what this writer wrote before a Group 10-era change: a
        // case-sensitive marker missed these entirely — Remove left them behind and the repair
        // would have added a duplicate instead of touching the entry that is there.
        WriteOurEntry("appname", $"\"{Prefix}savelocker\" ui", "");
        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Stale,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out _));

        Assert.True(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));
        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Current,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out var exe));
        Assert.Contains("\" open", exe);
    }

    [Fact]
    public void AnEntrySpelledAppName_IsStillFound_AndRemoved()
    {
        WriteOurEntry("AppName", $"\"{Prefix}savelocker\" ui", "");
        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Stale,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out _));
        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckApp));
    }

    /// <summary>Two of our entries under one name — what the case-mismatched writer left behind.</summary>
    private static byte[] TwoEntries(string secondExe)
    {
        static byte[] S(string s) => Encoding.UTF8.GetBytes(s + "\0");
        return new byte[][]
        {
            [0x00], S("shortcuts"),
            [0x00], S("0"),
            [0x02], S("appid"), BitConverter.GetBytes(-4242),
            [0x01], S("appname"), S("SaveLocker"),
            [0x01], S("Exe"), S($"\"{Prefix}savelocker\" ui"),
            [0x00], S("tags"), [0x08],
            [0x08],
            [0x00], S("1"),
            [0x02], S("appid"), BitConverter.GetBytes(-4243),
            [0x01], S("AppName"), S("SaveLocker"),
            [0x01], S("Exe"), S(secondExe),
            [0x00], S("tags"), [0x08],
            [0x08],
            [0x08], [0x08],
        }.SelectMany(b => b).ToArray();
    }

    [Fact]
    public void RemovingDuplicates_RemovesEveryCopyThatIsOurs()
    {
        File.WriteAllBytes(Vdf, TwoEntries($"\"{Prefix}savelocker\" open"));

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckApp));
        Assert.DoesNotContain("SaveLocker", Encoding.UTF8.GetString(File.ReadAllBytes(Vdf)));
    }

    [Fact]
    public void RemovingDuplicates_Refuses_WhenOneOfThemIsNotOurs()
    {
        var before = TwoEntries("\"/usr/bin/their-game\"");
        File.WriteAllBytes(Vdf, before);

        Assert.False(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckApp));
        Assert.Equal(before, File.ReadAllBytes(Vdf));
    }

    [Fact]
    public void Add_WritesTheNameKeyTheWaySteamDoes()
    {
        AddUi();
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(Vdf));
        Assert.Contains("appname\0SaveLocker Test\0", text);
        Assert.DoesNotContain("AppName", text);
    }

    [Fact]
    public void Retarget_RepairsTheOldInstallShShape_WhereUiSitsInLaunchOptions()
    {
        // The pre-controller install.sh told people: Exe = the binary, Launch Options = "ui".
        WriteOurEntry("appname", $"\"{Prefix}savelocker\"", "ui");

        Assert.True(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));

        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Current,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out var exe));
        Assert.Equal($"\"{Prefix}savelocker\"", exe);   // the Exe field is untouched...
        Assert.Contains("LaunchOptions\0open\0", Encoding.UTF8.GetString(File.ReadAllBytes(Vdf)));
        Assert.False(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));   // idempotent
    }

    [Fact]
    public void Retarget_AppendsTheCommand_ForAHandAddedEntryWithNoOptions()
    {
        // The new install.sh's manual alternative: "Add a Non-Steam Game -> SaveLocker", no options.
        WriteOurEntry("appname", $"\"{Prefix}savelocker\"", "");

        Assert.True(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));
        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Current,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out var exe));
        Assert.Contains("\" open", exe);
    }

    [Fact]
    public void Retarget_KeepsTheRestOfTheEntry_AndRenamesHandSetArt()
    {
        var original = WriteUsersOwnFile();
        var legacy = new SteamShortcutWriter.Kind("SaveLocker", "ui");
        var oldId = SteamShortcutWriter.AddTo(Vdf, Prefix, false, legacy, null, null)!.Value;
        // Art set by hand (Steam's own custom-artwork flow lands here too) under the old AppID.
        Directory.CreateDirectory(Grid);
        foreach (var f in SteamArt.ShortcutFiles(unchecked((uint)oldId)))
            File.WriteAllBytes(Path.Combine(Grid, f.Name), [1, 2, 3]);

        Assert.True(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));

        // The person's own entry is untouched, and the repaired entry runs the new command.
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(Vdf));
        Assert.Contains("Their Game", text);
        Assert.Contains("SaveLocker\0", text);
        Assert.Contains("\" open", text);
        // The art followed the entry: old names gone, same number of files present.
        foreach (var name in SteamArt.ShortcutFiles(unchecked((uint)oldId)).Select(f => f.Name))
            Assert.False(File.Exists(Path.Combine(Grid, name)), name);
        Assert.Equal(4, Directory.GetFiles(Grid).Length);
        // The pre-SaveLocker backup is not touched by a repair (it still holds the original file).
        Assert.Equal(original, File.ReadAllBytes(Backup));
    }

    [Fact]
    public void Retarget_Refuses_AUserShortcutWithTheShippedName()
    {
        var foreign = WriteForeignSaveLocker();

        Assert.Equal(SteamShortcutWriter.ShortcutPresence.Foreign,
            SteamShortcutWriter.InspectAt(Vdf, SteamShortcutWriter.DeckApp, out _));
        Assert.False(SteamShortcutWriter.RetargetAt(Vdf, SteamShortcutWriter.DeckApp));
        Assert.Equal(foreign, File.ReadAllBytes(Vdf));
    }

    [Fact]
    public void RemovingTheShippedEntry_DeletesOnlyItsOwnArt()
    {
        var app = SteamShortcutWriter.AddTo(Vdf, Prefix, false, SteamShortcutWriter.DeckApp, null, null)!.Value;
        var test = SteamShortcutWriter.AddTo(Vdf, Prefix, false, SteamShortcutWriter.DeckAppTest, "--port 5177", null)!.Value;
        SteamShortcutWriter.PaintArt(Grid, unchecked((uint)app), "ember", "pixel", Layer);
        SteamShortcutWriter.PaintArt(Grid, unchecked((uint)test), "ember", "pixel", Layer);

        Assert.True(SteamShortcutWriter.RemoveFrom(Vdf, SteamShortcutWriter.DeckApp));
        Assert.All(Art(app), path => Assert.False(File.Exists(path), path));
        Assert.All(Art(test), path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void TheStartupPolicy_AddsOnce_RepairsAlways_AndKeepsARemoval()
    {
        // Current or foreign: never anything.
        Assert.Equal(DeckShortcut.Decision.Nothing, DeckShortcut.Decide(SteamShortcutWriter.ShortcutPresence.Current, false));
        Assert.Equal(DeckShortcut.Decision.Nothing, DeckShortcut.Decide(SteamShortcutWriter.ShortcutPresence.Foreign, false));
        // Stale: repaired whether or not it was offered before — the entry exists, nobody removed it.
        Assert.Equal(DeckShortcut.Decision.Repair, DeckShortcut.Decide(SteamShortcutWriter.ShortcutPresence.Stale, true));
        // Missing: added exactly once; a later removal from Steam stays a removal.
        Assert.Equal(DeckShortcut.Decision.Add, DeckShortcut.Decide(SteamShortcutWriter.ShortcutPresence.Missing, false));
        Assert.Equal(DeckShortcut.Decision.Nothing, DeckShortcut.Decide(SteamShortcutWriter.ShortcutPresence.Missing, true));
    }

    [Fact]
    public void OnlyTheInstalledAgentMayTouchARealLibrary()
    {
        Assert.True(DeckShortcut.IsInstalledAgent(SaveLocker.Agent.AgentConfig.DefaultDir, SaveLocker.Agent.AgentConfig.DefaultDir));
        Assert.False(DeckShortcut.IsInstalledAgent(Path.GetTempPath(), SaveLocker.Agent.AgentConfig.DefaultDir));
        Assert.False(DeckShortcut.IsInstalledAgent(SaveLocker.Agent.AgentConfig.DefaultDir, Path.GetTempPath()));
    }
}
