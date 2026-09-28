using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// What the heartbeat says this machine runs. The os-release samples are modelled on each distro's
/// file — the console draws a logo from <c>ID</c>/<c>VARIANT_ID</c>, so those must arrive intact.
/// </summary>
public class OsIdentityTests
{
    private const string SteamOs = """
        NAME="SteamOS"
        PRETTY_NAME="SteamOS"
        VERSION_CODENAME=holo
        ID=steamos
        ID_LIKE=arch
        ANSI_COLOR="1;35"
        HOME_URL="https://www.steampowered.com/"
        VARIANT_ID=steamdeck
        """;

    private const string Bazzite = """
        NAME="Bazzite"
        VERSION="42.20250901.0 (Kinoite)"
        ID=bazzite
        ID_LIKE="fedora"
        VARIANT_ID=bazzite-deck
        PRETTY_NAME="Bazzite 42 (FROM Fedora Kinoite)"
        """;

    private const string Ubuntu = """
        PRETTY_NAME="Ubuntu 24.04.1 LTS"
        NAME="Ubuntu"
        VERSION_ID="24.04"
        ID=ubuntu
        ID_LIKE=debian
        """;

    private const string Mint = """
        NAME="Linux Mint"
        VERSION="22 (Wilma)"
        ID=linuxmint
        ID_LIKE="ubuntu debian"
        PRETTY_NAME="Linux Mint 22"
        """;

    [Fact]
    public void A_steam_deck_reports_steamos_and_names_the_hardware()
    {
        var os = OsIdentity.FromOsRelease(OsIdentity.ParseOsRelease(SteamOs), "Valve", "Jupiter", wsl: false);

        Assert.Equal("steamos", os.Id);
        Assert.Equal("SteamOS", os.Name);
        Assert.Equal("arch", os.IdLike);
        Assert.Equal("steamdeck", os.VariantId);
        Assert.Equal("Steam Deck", os.Device);
    }

    [Theory]
    [InlineData("Jupiter", "Steam Deck")]
    [InlineData("Galileo", "Steam Deck OLED")]
    [InlineData("Something new", "Valve hardware")]
    public void Valve_boards_are_named(string product, string expected) =>
        Assert.Equal(expected, OsIdentity.DeviceName("Valve", product));

    [Theory]
    [InlineData("To Be Filled By O.E.M.", "To Be Filled By O.E.M.")]
    [InlineData("LENOVO", "83E1")]
    [InlineData(null, null)]
    public void Any_other_board_is_not_named(string? vendor, string? product) =>
        Assert.Null(OsIdentity.DeviceName(vendor, product));

    [Fact]
    public void Bazzite_keeps_its_variant_for_the_console()
    {
        var os = OsIdentity.FromOsRelease(OsIdentity.ParseOsRelease(Bazzite), "Valve", "Galileo", wsl: false);

        Assert.Equal("bazzite", os.Id);
        Assert.Equal("bazzite-deck", os.VariantId);
        Assert.Equal("fedora", os.IdLike);
        Assert.Equal("Bazzite 42 (FROM Fedora Kinoite)", os.Name);
        Assert.Equal("Steam Deck OLED", os.Device);
    }

    [Fact]
    public void Ubuntu_under_wsl_says_so()
    {
        var os = OsIdentity.FromOsRelease(OsIdentity.ParseOsRelease(Ubuntu), null, null, wsl: true);

        Assert.Equal("ubuntu", os.Id);
        Assert.Equal("Ubuntu 24.04.1 LTS", os.Name);
        Assert.Equal("WSL", os.Device);
    }

    [Fact]
    public void Id_like_keeps_every_parent()
    {
        var os = OsIdentity.FromOsRelease(OsIdentity.ParseOsRelease(Mint), null, null, wsl: false);

        Assert.Equal("linuxmint", os.Id);
        Assert.Equal("ubuntu debian", os.IdLike);
        Assert.Null(os.Device);
    }

    [Fact]
    public void Quoting_escapes_and_comments_follow_the_spec()
    {
        var kv = OsIdentity.ParseOsRelease("""
            # a comment
            PRETTY_NAME="Say \"hi\" \\ bye"
            NAME='Single quoted'

            BROKEN LINE
            ID=Arch
            """);

        Assert.Equal("Say \"hi\" \\ bye", kv["PRETTY_NAME"]);
        Assert.Equal("Single quoted", kv["NAME"]);
        Assert.Equal("Arch", kv["ID"]);
        Assert.Equal(3, kv.Count);
    }

    [Fact]
    public void A_missing_id_is_the_specs_default_and_the_name_falls_back()
    {
        var os = OsIdentity.FromOsRelease(OsIdentity.ParseOsRelease("NAME=Custom\n"), null, null, wsl: false);
        Assert.Equal("linux", os.Id);
        Assert.Equal("Custom", os.Name);

        var bare = OsIdentity.FromOsRelease(new Dictionary<string, string>(), null, null, wsl: false);
        Assert.Equal("linux", bare.Id);
        Assert.Equal("linux", bare.Name);
    }

    [Fact]
    public void Id_is_lowercased_as_the_spec_requires()
    {
        var os = OsIdentity.FromOsRelease(OsIdentity.ParseOsRelease("ID=Arch\nID_LIKE=\"Arch\"\n"), null, null, wsl: false);
        Assert.Equal("arch", os.Id);
        Assert.Equal("arch", os.IdLike);
    }

    [Theory]
    [InlineData(10, 0, 26200, "Windows 11 (build 26200)")]
    [InlineData(10, 0, 22000, "Windows 11 (build 22000)")]
    [InlineData(10, 0, 19045, "Windows 10 (build 19045)")]
    [InlineData(6, 3, 9600, "Windows 6.3 (build 9600)")]
    public void Windows_11_is_told_apart_by_build(int major, int minor, int build, string expected)
    {
        var os = OsIdentity.ForWindows(new Version(major, minor, build));
        Assert.Equal("windows", os.Id);
        Assert.Equal(expected, os.Name);
        Assert.Null(os.Device);
    }

    [Fact]
    public void This_machine_reports_something()
    {
        var os = OsIdentity.This;
        Assert.False(string.IsNullOrWhiteSpace(os.Id));
        Assert.False(string.IsNullOrWhiteSpace(os.Name));
        if (OperatingSystem.IsWindows()) Assert.Equal("windows", os.Id);
    }
}
