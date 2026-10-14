using System.Diagnostics;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Manifest-expanded paths are constrained, not trusted. A template that is not absolute, that
/// climbs with "..", or whose real (link-resolved) location leaves the resolver's own roots is
/// refused — the same outcome as any other unresolvable entry: nothing, so the user is asked to
/// pick a folder instead of being handed a confident path that is wrong.
/// </summary>
public sealed class ManifestPathConstraintTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-constrain-" + Guid.NewGuid().ToString("N"));

    private string Root => Path.Combine(_dir, "root");
    private string Roaming => Path.Combine(Root, "AppData", "Roaming");

    public ManifestPathConstraintTests() => Directory.CreateDirectory(Roaming);

    private readonly List<string> _links = new();

    public void Dispose()
    {
        // A recursive delete trips over a Windows junction (and must never walk one); remove the
        // links themselves first, then the ordinary tree.
        foreach (var link in _links)
        {
            try
            {
                if (OperatingSystem.IsWindows()) Directory.Delete(link, recursive: false);
                else File.Delete(link);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private PathResolver Resolver(bool caseInsensitive = false) =>
        new(new Dictionary<string, string>
        {
            ["<winAppData>"] = Roaming,
            ["<home>"] = Root,
            ["<base>"] = Path.Combine(_dir, "game"),
        }, caseInsensitiveFilesystem: caseInsensitive);

    /// <summary>A real folder under the test root, returned canonical.</summary>
    private string Folder(params string[] parts)
    {
        var dir = Path.Combine(new[] { _dir }.Concat(parts).ToArray());
        Directory.CreateDirectory(dir);
        return Path.GetFullPath(dir);
    }

    [Fact]
    public void A_traversal_that_reaches_an_existing_folder_outside_every_root_is_refused()
    {
        // Roaming/../../../escape lands on <tmp>/escape. The raw string still BEGINS with the
        // token's value, which is exactly what the old textual root check saw.
        Folder("escape");

        Assert.Null(Resolver().ResolveToDirectory("<winAppData>/../../../escape"));
    }

    [Fact]
    public void A_traversal_that_would_stay_inside_a_root_is_still_refused()
    {
        Folder("root", "AppData", "Roaming", "Inside");

        // Roaming/../Roaming/Inside collapses back under the token, but no well-formed entry
        // needs the detour — strict rejection is what makes the rule auditable.
        Assert.Null(Resolver().ResolveToDirectory("<winAppData>/../Roaming/Inside"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("escape")]
    [InlineData("/escape")]
    [InlineData(".")]
    public void A_template_that_is_not_an_absolute_anchored_path_is_refused(string template)
    {
        Folder("escape");

        Assert.Null(Resolver().ResolveToDirectory(template));
    }

    [Fact]
    public void A_hostile_manifest_entry_resolves_to_nothing_and_a_clean_one_still_resolves()
    {
        Folder("escape"); // the folder the hostile entry would land on if the traversal ran
        var clean = Folder("root", "AppData", "Roaming", "Clean");

        const string yaml = """
            Hostile Game:
              files:
                <winAppData>/../../../escape:
                  tags:
                    - save
            Clean Game:
              files:
                <winAppData>/Clean:
                  tags:
                    - save
            """;

        var manifest = ManifestLoader.Parse(yaml);

        Assert.Empty(manifest.ResolveSaveLocations("Hostile Game", Resolver()));
        Assert.Equal(clean, Assert.Single(manifest.ResolveSaveLocations("Clean Game", Resolver())).Directory);
    }

    [Fact]
    public void A_folder_reached_through_a_link_that_leaves_the_roots_is_refused()
    {
        var outside = Folder("outside");
        RequireDirLink(Path.Combine(Roaming, "Linked"), outside);

        var clean = Folder("root", "AppData", "Roaming", "Clean");

        // The link makes the textual path sit under <winAppData> while the real folder does not.
        Assert.Null(Resolver().ResolveToDirectory("<winAppData>/Linked"));
        Assert.Equal(clean, Resolver().ResolveToDirectory("<winAppData>/Clean"));
    }

    [Fact]
    public void A_link_chain_that_leaves_the_roots_is_refused()
    {
        var outside = Folder("outside");
        var hop = Path.Combine(_dir, "hop");
        RequireDirLink(hop, outside);
        RequireDirLink(Path.Combine(Roaming, "Chained"), hop);

        Assert.Null(Resolver().ResolveToDirectory("<winAppData>/Chained"));
    }

    [Fact]
    public void A_store_user_fanout_child_that_is_a_link_out_of_the_roots_is_refused()
    {
        var parent = Folder("root", "AppData", "Roaming", "Steam");
        var outside = Folder("outside");
        Folder("outside", "SaveGames");
        RequireDirLink(Path.Combine(parent, "12345"), outside);

        var real = Folder("root", "AppData", "Roaming", "Steam", "67890", "SaveGames");

        var dirs = Resolver().ResolveToDirectories("<winAppData>/Steam/<storeUserId>/SaveGames");

        Assert.Equal(new[] { real }, dirs);
    }

    [Fact]
    public void Ordinary_templates_still_resolve()
    {
        var saves = Folder("root", "AppData", "Roaming", "Game", "Saves");
        var install = Folder("game");
        var installSaves = Folder("game", "Saves");
        File.WriteAllText(Path.Combine(install, "Save.dat"), "x");

        Assert.Equal(saves, Resolver().ResolveToDirectory("<winAppData>/Game/Saves"));
        // A wildcard still trims to the deepest fixed directory, and a file template to its folder.
        Assert.Equal(saves, Resolver().ResolveToDirectory("<winAppData>/Game/Saves/*.sav"));
        Assert.Equal(install, Resolver().ResolveToDirectory("<base>/Save.dat"));
        Assert.Equal(installSaves, Resolver().ResolveToDirectory("<base>/Saves/*.sav"));
    }

    /// <summary>
    /// A junction on Windows (which needs no elevation, unlike a symlink) and a symlink on Unix —
    /// the same two mechanisms <c>run-hardening-tests.ps1</c> uses for its own link checks.
    /// </summary>
    private void RequireDirLink(string link, string target)
    {
        Assert.True(TryCreateDirLink(link, target),
            $"this machine could not create a directory link {link} -> {target}; the link-escape checks could not run");
        _links.Add(link);
    }

    private static bool TryCreateDirLink(string link, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                })!;
                p.WaitForExit();
                return p.ExitCode == 0 && Directory.Exists(link);
            }

            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch { return false; }
    }
}
