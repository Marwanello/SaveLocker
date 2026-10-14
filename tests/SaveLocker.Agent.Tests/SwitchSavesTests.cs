using System.Buffers.Binary;
using System.Text;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>Builds the Switch emulators' files the way the real ones are laid out (captured 2026-10-13, PC and Deck).</summary>
internal static class SwitchFixtures
{
    /// <summary>A Ryujinx <c>imkvdb.arc</c>: <c>IMKV</c>, a count, then <c>IMEN</c> entries of a 0x40 key and a 0x40 value.</summary>
    public static byte[] Index(params (string TitleId, string UserId, RyujinxSaveIndex.SaveType Type, ulong SaveId)[] entries)
    {
        var data = new List<byte>();
        data.AddRange("IMKV"u8.ToArray());
        data.AddRange(new byte[4]);
        data.AddRange(U32((uint)entries.Length));
        foreach (var e in entries)
        {
            data.AddRange("IMEN"u8.ToArray());
            data.AddRange(U32(0x40));
            data.AddRange(U32(0x40));
            var key = new byte[0x40];
            BinaryPrimitives.WriteUInt64LittleEndian(key, Convert.ToUInt64(e.TitleId, 16));
            BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(8), Convert.ToUInt64(e.UserId[..16], 16));
            BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(16), Convert.ToUInt64(e.UserId[16..], 16));
            key[0x20] = (byte)e.Type;
            data.AddRange(key);
            var value = new byte[0x40];
            BinaryPrimitives.WriteUInt64LittleEndian(value, e.SaveId);
            value[0x18] = 1;
            data.AddRange(value);
        }
        return data.ToArray();
    }

    /// <summary>A Yuzu fork's <c>profiles.dat</c>: a 0x10 header, then eight 0xC8-byte users (UUID, copy, timestamp, name).
    /// <paramref name="folder"/> is the save folder's name, the UUID's bytes reversed.</summary>
    public static byte[] Profiles(params (string Folder, string Name)[] users)
    {
        var data = new byte[0x10 + 8 * 0xC8];
        for (var i = 0; i < users.Length; i++)
        {
            var uuid = Convert.FromHexString(users[i].Folder);
            Array.Reverse(uuid);
            var at = 0x10 + i * 0xC8;
            uuid.CopyTo(data, at);
            uuid.CopyTo(data, at + 16);
            Encoding.UTF8.GetBytes(users[i].Name).CopyTo(data, at + 0x28);
        }
        return data;
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public static void Write(string path, string text) => Write(path, Encoding.UTF8.GetBytes(text));

    /// <summary>EmuDeck's Eden links under <paramref name="emulation"/>, with one user.</summary>
    public static SwitchEmulators.Setup Eden(string emulation, string user, string userName = "Eden")
    {
        Write(Path.Combine(emulation, "saves", "eden", "profiles", "profiles.dat"), Profiles((user, userName)));
        Directory.CreateDirectory(Path.Combine(emulation, "saves", "eden", "saves", "0000000000000000"));
        return new SwitchEmulators.Setup("Eden", Path.Combine(emulation, "saves", "eden", "saves"),
            Profiles: Path.Combine(emulation, "saves", "eden", "profiles", "profiles.dat"), EmuDeck: true);
    }

    public static string EdenSave(string emulation, string user, string titleId) =>
        Path.Combine(emulation, "saves", "eden", "saves", "0000000000000000", user, titleId);

    /// <summary>A Ryujinx portable folder with its index, users and the given saves (each <c>&lt;id&gt;/0</c> and
    /// <c>ExtraData0</c>, as Ryujinx makes them).</summary>
    public static SwitchEmulators.Setup Ryujinx(string dir, string currentUser,
        params (string TitleId, string UserId, RyujinxSaveIndex.SaveType Type, ulong SaveId)[] entries)
    {
        Write(Path.Combine(dir, "bis", "system", "save", "8000000000000000", "0", "imkvdb.arc"), Index(entries));
        Write(Path.Combine(dir, "system", "Profiles.json"),
            $$"""{"profiles":[{"user_id":"{{currentUser}}","name":"RyuPlayer","account_state":"Open"}],"last_opened":"{{currentUser}}"}""");
        foreach (var e in entries)
        {
            var save = Path.Combine(dir, "bis", "user", "save", e.SaveId.ToString("x16"));
            Directory.CreateDirectory(Path.Combine(save, "0"));
            Directory.CreateDirectory(Path.Combine(save, "1"));
            Write(Path.Combine(save, "ExtraData0"), new byte[512]);
        }
        return new SwitchEmulators.Setup("Ryujinx", Path.Combine(dir, "bis", "user", "save"),
            Profiles: Path.Combine(dir, "system", "Profiles.json"), SystemSaves: Path.Combine(dir, "bis", "system", "save"),
            Games: Path.Combine(dir, "games"));
    }

    public static string RyujinxSave(string dir, ulong saveId, string copy = "0") =>
        Path.Combine(dir, "bis", "user", "save", saveId.ToString("x16"), copy);
}

/// <summary>The Switch readers (tasks/emulator-saves Phase 6) against the files captured on the PC and the Deck.</summary>
public sealed class SwitchSavesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-switch-" + Guid.NewGuid().ToString("N"));

    public SwitchSavesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string rel) => Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));

    private const string RyuDefaultUser = "00000000000000010000000000000000";
    private const string Mp4 = "010019A01E2F2000";
    private const string Acnh = "01006F8002326000";

    /// <summary>The PC's real <c>imkvdb.arc</c> (EmuDeck for Windows' Ryujinx, 2026-10-13): Mii data, Metroid Prime 4's
    /// account save, Animal Crossing's BCAT and device saves.</summary>
    private const string PcIndex = "494D4B560000000004000000494D454E40000000400000000000000000000000000000000000000000000000000000003000000000000080000000000000000000000000000000000000000000000000000000000000000030000000000000800000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000494D454E400000004000000000202F1EA0190001010000000000000000000000000000000000000000000000010000000000000000000000000000000000000000000000000000000000000003000000000000000000000000000000000000000000000001000000000000000000000000000000000000000000000000000000000000000000000000000000494D454E400000004000000000603202806F0001000000000000000000000000000000000000000000000000020000000000000000000000000000000000000000000000000000000000000002000000000000000000000000000000000000000000000001000000000000000000000000000000000000000000000000000000000000000000000000000000494D454E400000004000000000603202806F0001000000000000000000000000000000000000000000000000030000000000000000000000000000000000000000000000000000000000000001000000000000000000000000000000000000000000000001000000000000000000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void The_real_ryujinx_index_names_each_saves_title_user_and_kind()
    {
        var entries = RyujinxSaveIndex.Parse(Convert.FromHexString(PcIndex));

        Assert.Equal(
        [
            new RyujinxSaveIndex.Entry(Mp4, RyuDefaultUser, RyujinxSaveIndex.SaveType.Account, 3),
            new RyujinxSaveIndex.Entry(Acnh, "00000000000000000000000000000000", RyujinxSaveIndex.SaveType.Bcat, 2),
            new RyujinxSaveIndex.Entry(Acnh, "00000000000000000000000000000000", RyujinxSaveIndex.SaveType.Device, 1),
        ], entries);
        Assert.Equal("0000000000000003", entries[0].Folder);
        Assert.Empty(RyujinxSaveIndex.Parse("not an index"u8));
    }

    [Fact]
    public void A_yuzu_forks_profile_folder_is_its_uuid_reversed()
    {
        // The PC's Eden user, as profiles.dat stores it and as the save folder is named.
        var raw = new byte[0x10 + 8 * 0xC8];
        Convert.FromHexString("ca0ef0a0d640cd77f2f6d1148babd7c4").CopyTo(raw, 0x10);
        "Eden"u8.ToArray().CopyTo(raw, 0x10 + 0x28);
        SwitchFixtures.Write(P("profiles.dat"), raw);

        Assert.Equal([new SwitchEmulators.User("C4D7AB8B14D1F6F277CD40D6A0F00ECA", "Eden")], SwitchEmulators.YuzuUsers(P("profiles.dat")));
    }

    [Fact]
    public void One_row_per_title_the_copy_written_last_named_by_ryujinx_or_the_rom()
    {
        const string user = "C4D7AB8B14D1F6F277CD40D6A0F00ECA";
        var eden = SwitchFixtures.Eden(P("Emulation"), user);
        // Eden: Metroid Prime 4 (older) beside Yuzu's mangled leftover, which holds only the save size.
        SwitchFixtures.Write(Path.Combine(SwitchFixtures.EdenSave(P("Emulation"), user, Mp4), "save.bin"), "eden");
        File.SetLastWriteTimeUtc(Path.Combine(SwitchFixtures.EdenSave(P("Emulation"), user, Mp4), "save.bin"), DateTime.UtcNow.AddDays(-2));
        SwitchFixtures.Write(P($"Emulation/saves/eden/saves/0000000000000000/F277CD40D6A0F00ECA00000000000000/0100000000010000/.yuzu_save_size"), new byte[16]);
        // Ryujinx: the same title, newer; Animal Crossing's device save; and a slot it made but never wrote.
        var ryu = SwitchFixtures.Ryujinx(P("ryu"), RyuDefaultUser,
            (Mp4, RyuDefaultUser, RyujinxSaveIndex.SaveType.Account, 3),
            (Acnh, "00000000000000000000000000000000", RyujinxSaveIndex.SaveType.Device, 1),
            ("0100000000010000", RyuDefaultUser, RyujinxSaveIndex.SaveType.Account, 4));
        SwitchFixtures.Write(Path.Combine(SwitchFixtures.RyujinxSave(P("ryu"), 3), "save.bin"), "ryu");
        SwitchFixtures.Write(Path.Combine(SwitchFixtures.RyujinxSave(P("ryu"), 1), "main.dat"), "island");
        SwitchFixtures.Write(P($"ryu/games/{Mp4.ToLowerInvariant()}/gui/metadata.json"), """{"title":"Metroid Prime™ 4: Beyond"}""");
        // A ROM in EmuDeck's switch/yuzu folder whose title saved nowhere here.
        SwitchFixtures.Write(P("Emulation/roms/switch/yuzu/Metroid Dread [010093801237C000][v0].nsp"), "rom");

        var found = SwitchEmulators.Scan([eden, ryu], [P("Emulation")]);

        // Animal Crossing has no ROM or Ryujinx record here: the eShop's name, from the bundled table.
        Assert.Equal(["Animal Crossing: New Horizons", "Metroid Dread", "Metroid Prime 4: Beyond"], found.Select(c => c.Name));
        var mp4 = found.Single(c => c.EmulatorRom == Mp4);
        Assert.Equal("Ryujinx", mp4.EmulatorName);
        Assert.Equal(SwitchFixtures.RyujinxSave(P("ryu"), 3), mp4.SuggestedSaveDir);
        Assert.Null(mp4.IncludeGlobs);
        Assert.Equal($"switch:{Mp4}", mp4.EmulatorKey);
        Assert.Equal(("Nintendo Switch", null, Mp4), (mp4.Release!.Console, mp4.Release.Region, Assert.Single(mp4.Release.GameIds!)));
        // A device-only game is its device save.
        Assert.Equal(SwitchFixtures.RyujinxSave(P("ryu"), 1), found.Single(c => c.EmulatorRom == Acnh).SuggestedSaveDir);
        // No save yet: under Eden's current user, where Eden will look for it.
        var dread = found.Single(c => c.EmulatorRom == "010093801237C000");
        Assert.True(dread.NoSaveYet);
        Assert.Equal("Eden", dread.EmulatorName);
        Assert.Equal(SwitchFixtures.EdenSave(P("Emulation"), user, "010093801237C000"), dread.SuggestedSaveDir);
        Assert.DoesNotContain(found, c => c.EmulatorRom == "0100000000010000");
    }

    [Fact]
    public void An_account_save_beside_a_device_save_declares_the_device_folder()
    {
        const string user = "DBBE23FA78D7168D745B090673B123AC";
        var eden = SwitchFixtures.Eden(P("Emulation"), user);
        SwitchFixtures.Write(Path.Combine(SwitchFixtures.EdenSave(P("Emulation"), user, Mp4), "save.bin"), "account");
        SwitchFixtures.Write(Path.Combine(SwitchFixtures.EdenSave(P("Emulation"), SwitchEmulators.DeviceProfile, Mp4), "device.bin"), "device");

        var c = Assert.Single(SwitchEmulators.Scan([eden], []));

        Assert.Equal(SwitchFixtures.EdenSave(P("Emulation"), user, Mp4), c.SuggestedSaveDir);
        var device = Assert.Single(c.ExtraSaveDirs!);
        Assert.Equal((SwitchEmulators.DeviceFolderKey, SwitchFixtures.EdenSave(P("Emulation"), SwitchEmulators.DeviceProfile, Mp4)), (device.Key, device.Dir));
    }

    [Theory]
    [InlineData("Metroid Prime™ 4꞉ Beyond [010019A01E2F2000][v0][Base][XCI]", "Metroid Prime 4: Beyond")]
    [InlineData("Pokemon Legends Arceus [01001F5010DFA000][v0](ROMSLAB)", "Pokemon Legends Arceus")]
    [InlineData("Pokémon Legends: Arceus", "Pokémon Legends: Arceus")]
    public void Titles_are_cleaned(string raw, string clean) => Assert.Equal(clean, SwitchEmulators.CleanTitle(raw));

    [Fact]
    public void Ryujinx_has_no_slot_until_the_game_made_one_and_a_fork_always_has_one()
    {
        var eden = SwitchFixtures.Eden(P("Emulation"), "C4D7AB8B14D1F6F277CD40D6A0F00ECA");
        var ryu = SwitchFixtures.Ryujinx(P("ryu"), RyuDefaultUser);

        Assert.Null(SwitchEmulators.Slot(ryu, Mp4));
        var slot = SwitchEmulators.Slot(eden, Mp4)!;
        Assert.False(slot.Exists);
        Assert.Equal("Eden", slot.UserName);
    }
}
