using System.Text;
using SaveLocker.Agent.Linux;
using SaveLocker.Agent.Linux.Art;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The test rig's two Steam shortcuts ("Conflict Game" and "SaveLocker Test") share one shortcuts.vdf, one
/// backup and one grid folder: removing either must leave the other — and the person's own shortcuts — intact.
/// </summary>
public sealed class DevSteamShortcutTests : IDisposable
{
    // The trailing slash keeps Path.Combine from joining with '\' when the tests run on Windows.
    private const string Prefix = "/home/deck/savelocker-test/";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "savelocker-shortcut-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "userdata", "1", "config");
    private string Vdf => Path.Combine(Config, "shortcuts.vdf");
    private string Grid => Path.Combine(Config, "grid");
    private string Backup => Vdf + ".savelocker-backup";

    public DevSteamShortcutTests() => Directory.CreateDirectory(Config);

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
        var appId = DevSteamShortcut.AddTo(Vdf, Prefix, false, DevSteamShortcut.DeckUi, "--port 5177", "%command%");
        Assert.NotNull(appId);
        DevSteamShortcut.PaintArt(Grid, unchecked((uint)appId.Value), "ember", "pixel", Layer);
        return appId.Value;
    }

    private void AddConflict() => Assert.NotNull(DevSteamShortcut.AddTo(Vdf, Prefix, false, DevSteamShortcut.Conflict, null, null));

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

        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.Conflict));
        Assert.All(Art(ui), path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void RemovingTheUiEntry_DeletesItsArt_EvenWhenTheFileItCreatedGoesToo()
    {
        var ui = AddUi();   // no shortcuts.vdf before: SaveLocker creates it

        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.DeckUi));
        Assert.False(File.Exists(Vdf));
        Assert.All(Art(ui), path => Assert.False(File.Exists(path), path));
        Assert.False(Directory.Exists(Grid));
    }

    [Fact]
    public void RemovingAnEntryThatIsNotThere_WhileTheOtherIs_IsNotARefusal()
    {
        var original = WriteUsersOwnFile();
        AddConflict();

        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.DeckUi));
        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.Conflict));
        Assert.Equal(original, File.ReadAllBytes(Vdf));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void TheBackup_StaysThePreSaveLockerFile_UntilTheLastTestEntryGoes()
    {
        var original = WriteUsersOwnFile();
        AddUi();
        AddConflict();

        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.DeckUi));   // testenv up removes and re-adds it
        Assert.Equal(original, File.ReadAllBytes(Backup));
        AddUi();
        Assert.Equal(original, File.ReadAllBytes(Backup));

        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.Conflict));
        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.DeckUi));
        Assert.Equal(original, File.ReadAllBytes(Vdf));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void AFileSaveLockerCreated_IsDeleted_OnceBothEntriesAreGone()
    {
        AddUi();
        AddConflict();

        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.DeckUi));
        Assert.True(File.Exists(Vdf));
        Assert.True(DevSteamShortcut.RemoveFrom(Vdf, DevSteamShortcut.Conflict));
        Assert.False(File.Exists(Vdf));
        Assert.False(File.Exists(Vdf + ".savelocker-created"));
        Assert.False(File.Exists(Backup));
    }
}
