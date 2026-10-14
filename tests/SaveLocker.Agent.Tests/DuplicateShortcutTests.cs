using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The one duplicate-shortcut rule both scanners and Doctor share (SteamShortcuts.DuplicateNameGroups /
/// DuplicatesFor): which same-named shortcuts count as duplicates, and which entry the wire marks tracked.
/// </summary>
public sealed class DuplicateShortcutTests
{
    private static SteamShortcut Shortcut(string name, string? appId, string? moonDeck = null) =>
        new(name, Exe: null, StartDir: null, AppId: appId, MoonDeckAppId: moonDeck);

    [Fact]
    public void SameName_DifferentAppIds_IsOneGroup()
    {
        var groups = SteamShortcuts.DuplicateNameGroups(new[]
        {
            Shortcut("MoonDeck Streamed Game", "1611110001"),
            Shortcut("MoonDeck Streamed Game", "1622220002"),
            Shortcut("Other Game", "1"),
        });

        var group = Assert.Single(groups);
        Assert.Equal("MoonDeck Streamed Game", group.Name);
        Assert.Equal(2, group.Entries.Count);
    }

    [Fact]
    public void NamesNormaliseTogether_PunctuationAndCaseIncluded()
    {
        // The exact drift the scan's own dedupe keys on: "HD 2D" vs "HD-2D", upper vs lower.
        var groups = SteamShortcuts.DuplicateNameGroups(new[]
        {
            Shortcut("DRAGON QUEST III HD 2D Remake", "1"),
            Shortcut("Dragon Quest III HD-2D Remake", "2"),
        });

        Assert.Single(groups);
    }

    [Fact]
    public void SameAppIdTwice_IsNotADuplicate()
    {
        // Steam assigns the same id to two entries added from one exe: one shortcut, not a warning.
        Assert.Empty(SteamShortcuts.DuplicateNameGroups(new[]
        {
            Shortcut("Their Game", "123"),
            Shortcut("Their Game", "123"),
        }));
    }

    [Fact]
    public void ANameThatNormalisesToNothing_IsNotAGroup()
    {
        Assert.Empty(SteamShortcuts.DuplicateNameGroups(new[]
        {
            Shortcut("!!!", "1"),
            Shortcut("---", "2"),
        }));
    }

    [Fact]
    public void DuplicatesFor_KeepsOneEntryPerAppId_InFileOrder_AndMarksTheTrackedOne()
    {
        var entries = SteamShortcuts.DuplicatesFor(new[]
        {
            Shortcut("Game", "100"),
            Shortcut("Game", "200"),
            Shortcut("Game", "100"),
        }, trackedAppId: "100");

        Assert.Equal(2, entries.Count);
        Assert.Equal(new DuplicateShortcut("100", "Game", Tracked: true), entries[0]);
        Assert.Equal(new DuplicateShortcut("200", "Game", Tracked: false), entries[1]);
    }

    [Fact]
    public void AShortcutWithNoAppId_IsItsOwnEntry_AndTrackable()
    {
        var entries = SteamShortcuts.DuplicatesFor(new[]
        {
            Shortcut("Game", null),
            Shortcut("Game", "200"),
        }, trackedAppId: null);

        Assert.Equal(new DuplicateShortcut(null, "Game", Tracked: true), entries[0]);
        Assert.Equal(new DuplicateShortcut("200", "Game", Tracked: false), entries[1]);
    }

    [Fact]
    public void AnAppIdNotInTheGroup_MarksNothingTracked()
    {
        // A confirmed MoonDeck install follows the real AppID, which is not one of the shortcut ids:
        // the wire must then claim no shortcut is tracked rather than pick one.
        var entries = SteamShortcuts.DuplicatesFor(new[]
        {
            Shortcut("Game", "1611110001"),
            Shortcut("Game", "1622220002"),
        }, trackedAppId: "1633330");

        Assert.DoesNotContain(entries, e => e.Tracked);
    }
}
