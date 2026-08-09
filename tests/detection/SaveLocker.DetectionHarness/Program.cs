using SaveLocker.DetectionHarness;
using SaveLocker.Shared;

// Save-path autodetection harness.
//
//   coverage  — analytic, no filesystem: how much of the manifest each token set can resolve.
//   sweep     — materialise dummy save trees for a seeded sample of REAL manifest games, then
//               score the production resolver against them.
//   pinned    — the same, for named games whose specific quirks must never regress.
//
// Modes 2 and 3 need no Steam, no Proton and no Deck: the resolver only reads a token map and the
// filesystem, and Oracle fakes both. See tests/detection/README.md.

var mode = args.Length > 0 ? args[0] : "help";
var opts = ParseOpts(args.Skip(1));

var manifestPath = opts.GetValueOrDefault("manifest")
    ?? Path.Combine(AppContext.BaseDirectory, "manifest.yaml");
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"Manifest not found: {manifestPath}");
    Console.Error.WriteLine("Pass --manifest <path>, or run tests/detection/run-detection-tests.sh which fetches it.");
    return 2;
}

var workRoot = opts.GetValueOrDefault("root")
    ?? Path.Combine(Path.GetTempPath(), "savelocker-detection");

// Linux fixtures live on a case-sensitive filesystem, so path comparison must be too — comparing
// case-insensitively here would hide exactly the Deck-only casing misses this harness exists to catch.
var pathCmp = OperatingSystem.IsWindows()
    ? StringComparer.OrdinalIgnoreCase
    : StringComparer.Ordinal;

Console.Error.WriteLine($"Loading manifest: {manifestPath}");
var yaml = File.ReadAllText(manifestPath);
var rich = ManifestModel.Parse(yaml);
Console.Error.WriteLine($"Parsed {rich.Count} entries.");

return mode switch
{
    "coverage" => Coverage(),
    "sweep" => Sweep(),
    "pinned" => Pinned(),
    _ => Help(),
};

int Help()
{
    Console.WriteLine("""
        usage: detection-harness <mode> [--manifest PATH] [--root DIR] [options]

          coverage                          Token-set coverage over the whole manifest.
          sweep     [--sample N] [--seed S] Materialise + resolve a random sample.
          pinned    [--cases FILE]          Materialise + resolve named regression cases.
        """);
    return 0;
}

// ---------------------------------------------------------------------------------------------
// coverage — pure analysis. Answers "how much would supporting token X buy us?"
// ---------------------------------------------------------------------------------------------
int Coverage()
{
    // What PathResolver implements today.
    string[] supported =
    [
        "<winAppData>", "<winLocalAppData>", "<winLocalAppDataLow>", "<winDocuments>",
        "<winPublic>", "<winProgramData>", "<winDir>", "<home>", "<osUserName>", "<winSavedGames>",
    ];

    var withSaves = rich.Where(kv => kv.Value.WindowsSaveTemplates.Any()).ToList();
    var tokenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    int now = 0, plusBase = 0, plusRootUser = 0, unresolvable = 0;

    foreach (var (_, game) in withSaves)
    {
        var templates = game.WindowsSaveTemplates.ToList();
        foreach (var t in templates)
            foreach (var tok in Tokens(t))
                tokenCounts[tok] = tokenCounts.GetValueOrDefault(tok) + 1;

        bool CanDo(IEnumerable<string> set) =>
            templates.Any(t => Tokens(t).All(set.Contains));

        if (CanDo(supported)) now++;
        else if (CanDo(supported.Append("<base>"))) plusBase++;
        else if (CanDo(supported.Concat(["<base>", "<root>", "<storeUserId>"]))) plusRootUser++;
        else unresolvable++;
    }

    Console.WriteLine($"Manifest entries                : {rich.Count}");
    Console.WriteLine($"Entries with a Windows SAVE path: {withSaves.Count}");
    Console.WriteLine();
    Console.WriteLine("Token frequency across those save paths:");
    foreach (var (tok, n) in tokenCounts.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  {n,8}  {tok,-22} {(supported.Contains(tok, StringComparer.OrdinalIgnoreCase) ? "supported" : "NOT SUPPORTED")}");
    Console.WriteLine();
    Console.WriteLine("Games resolvable, by token set:");
    Console.WriteLine($"  {now,8}  today");
    Console.WriteLine($"  {plusBase,8}  unlocked by adding <base>");
    Console.WriteLine($"  {plusRootUser,8}  unlocked by adding <root> + <storeUserId>");
    Console.WriteLine($"  {unresolvable,8}  still unresolvable");
    return 0;
}

// ---------------------------------------------------------------------------------------------
// sweep / pinned — build real fixture trees, then score the production resolver.
// ---------------------------------------------------------------------------------------------
int Sweep()
{
    var sample = int.Parse(opts.GetValueOrDefault("sample") ?? "300");
    var seed = int.Parse(opts.GetValueOrDefault("seed") ?? "1");

    // Seeded and name-ordered so a run is reproducible: an unstable sample would make the pass
    // rate wander between runs and the suite would be useless as a regression gate.
    var pool = rich.Where(kv => kv.Value.WindowsSaveTemplates.Any())
                   .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                   .ToList();
    var rng = new Random(seed);
    var chosen = pool.OrderBy(_ => rng.Next()).Take(sample).ToList();

    Console.WriteLine($"Sweep: {chosen.Count} games (seed {seed})");
    Console.WriteLine();

    var results = chosen.Select(kv => Evaluate(kv.Key, kv.Value)).ToList();
    Report(results);
    // A sweep measures; it does not gate. Regressions are gated by `pinned`, which is why this
    // returns success even when the hit rate is low.
    return 0;
}

int Pinned()
{
    var casesFile = opts.GetValueOrDefault("cases")
        ?? Path.Combine(AppContext.BaseDirectory, "pinned-cases.tsv");
    if (!File.Exists(casesFile))
    {
        Console.Error.WriteLine($"Cases file not found: {casesFile}");
        return 2;
    }

    var failures = 0;
    Console.WriteLine("Pinned regression cases:");
    Console.WriteLine();

    foreach (var line in File.ReadAllLines(casesFile))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
        var parts = line.Split('\t', StringSplitOptions.TrimEntries);
        var name = parts[0];
        var expect = parts.Length > 1 ? parts[1] : "HIT";
        var why = parts.Length > 2 ? parts[2] : "";
        var alias = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null;

        if (!rich.TryGetValue(name, out var game))
        {
            Console.WriteLine($"  FAIL  {name}\n        not present in the manifest at all");
            failures++;
            continue;
        }

        var r = Evaluate(name, game, alias);
        var ok = r.Outcome.StartsWith(expect, StringComparison.Ordinal);
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(alias is null ? "" : $"  (asked as \"{alias}\")")}");
        Console.WriteLine($"        expected {expect}, got {r.Outcome}   {why}");
        if (!ok && r.Expected.Count > 0)
            Console.WriteLine($"        wanted:   {string.Join("\n                  ", r.Expected)}");
        if (!ok && r.Returned.Count > 0)
            Console.WriteLine($"        returned: {string.Join("\n                  ", r.Returned)}");
        if (!ok) failures++;
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0 ? "All pinned cases passed." : $"{failures} pinned case(s) FAILED.");
    return failures == 0 ? 0 : 1;
}

// ---------------------------------------------------------------------------------------------

// lookupName lets a case build fixtures from the manifest's own key while asking production for a
// DIFFERENT spelling — the real shape of a non-Steam shortcut, whose name is whatever the user typed.
Result Evaluate(string name, ManifestModel.Game game, string? lookupName = null)
{
    // One fixture tree per game, keyed by a filesystem-safe form of its name. Games share nothing,
    // so one game's stray directory can never make another look like a hit.
    var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    // Windows silently DROPS trailing dots and spaces from directory names, so "F.E.A.R." on disk
    // is "F.E.A.R". Expected paths are built by string concat and returned paths come back through
    // the filesystem, so without this every game whose name ends in a dot is a phantom failure.
    safe = safe.TrimEnd('.', ' ');
    var fixtureRoot = Path.Combine(workRoot, safe);
    if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true);

    var appId = (game.Steam?.Id ?? 900000 + Math.Abs(name.GetHashCode() % 90000)).ToString();
    const string storeUserId = "1234567";
    var oracle = Oracle.ForProtonFixture(fixtureRoot, appId, game.InstallDirName, storeUserId);

    var expected = new List<string>();
    foreach (var t in game.WindowsSaveTemplates)
        if (oracle.Materialise(t) is { } dir) expected.Add(Norm(dir));

    // Config-only paths are materialised TOO, on purpose. A real install has them, and returning
    // one instead of the save folder is the DRAGON QUEST III failure — invisible unless the
    // resolver is made to face the same choice it faces on a real machine.
    var configDirs = new List<string>();
    foreach (var t in game.WindowsConfigOnlyTemplates)
        if (oracle.Materialise(t) is { } dir) configDirs.Add(Norm(dir));

    // --- the code under test ---
    var loader = ManifestLoader.Parse(SingleEntryYaml(name, game));
    // Mirrors what LinuxGameScanner now supplies for a real game: the prefix plus the three
    // per-game values (<base>, <root>, <storeUserId>) that used to be unavailable.
    var resolver = PathResolver.Proton(
        oracle.CompatData,
        installDir: oracle.InstallBase,
        storeRoot: oracle.SteamRoot,
        storeUserId: oracle.StoreUserId);
    var returned = loader.ResolveSaveDirectories(lookupName ?? name, resolver).Select(Norm).ToList();

    var outcome =
        expected.Count == 0 ? "SKIP(no expandable save path)"
        : returned.Any(r => expected.Contains(r, pathCmp)) ? "HIT"
        : returned.Count > 0 && returned.All(r => configDirs.Contains(r, pathCmp)) ? "WRONG(config folder)"
        : returned.Count > 0 ? "WRONG(unrelated folder)"
        : $"MISS({BlockingTokens(game, oracle)})";

    return new Result(name, outcome, expected, returned);
}

string BlockingTokens(ManifestModel.Game game, Oracle oracle)
{
    // What PathResolver cannot expand — reported so a MISS names its cause instead of just failing.
    string[] unsupported = ["<base>", "<root>", "<storeUserId>"];
    var hit = game.WindowsSaveTemplates
        .SelectMany(Tokens)
        .Where(t => unsupported.Contains(t, StringComparer.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    return hit.Count > 0 ? string.Join("+", hit) : "no unsupported token — investigate";
}

// ManifestLoader takes YAML, so the single game under test is re-serialised rather than the whole
// 17 MB manifest being reparsed per game.
//
// tags and `when` MUST be carried through. They used to be dropped — every entry was written as
// "path": {} — which silently disabled the production tag filter this harness exists to verify:
// production saw untagged paths, treated them all as saves, and happily returned config folders
// while the harness scored them however it liked. A harness that disables the feature under test is
// worse than no harness, because it reports success.
string SingleEntryYaml(string name, ManifestModel.Game game)
{
    string Q(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"");

    var sb = new System.Text.StringBuilder();
    sb.Append('"').Append(Q(name)).Append("\":\n");
    sb.Append("  files:\n");
    foreach (var (template, entry) in game.Files ?? new())
    {
        sb.Append("    \"").Append(Q(template)).Append("\":\n");

        if (entry?.Tags is { Count: > 0 } tags)
        {
            sb.Append("      tags:\n");
            foreach (var tag in tags) sb.Append("        - \"").Append(Q(tag)).Append("\"\n");
        }

        if (entry?.When is { Count: > 0 } when)
        {
            sb.Append("      when:\n");
            foreach (var w in when)
            {
                sb.Append("        -");
                if (w?.Os is { Length: > 0 } os) sb.Append(" os: \"").Append(Q(os)).Append('"');
                else sb.Append(" {}");
                sb.Append('\n');
            }
        }

        // A key with neither must still parse as a mapping entry: rewrite "key:\n" as "key: {}".
        if (entry?.Tags is not { Count: > 0 } && entry?.When is not { Count: > 0 })
        {
            sb.Length -= 1;
            sb.Append(" {}\n");
        }
    }
    return sb.ToString();
}

void Report(List<Result> results)
{
    foreach (var g in results.GroupBy(r => r.Outcome.Split('(')[0]).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Count(),6}  {g.Key}");

    var misses = results.Where(r => r.Outcome.StartsWith("MISS")).ToList();
    if (misses.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Miss causes:");
        foreach (var g in misses.GroupBy(r => r.Outcome).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Count(),6}  {g.Key}");
        Console.WriteLine();
        Console.WriteLine("Sample misses:");
        foreach (var r in misses.Take(10)) Console.WriteLine($"    {r.Name}  ->  {r.Outcome}");
    }

    var wrong = results.Where(r => r.Outcome.StartsWith("WRONG")).ToList();
    if (wrong.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("WRONG answers (worse than a miss — the user is told to trust these):");
        foreach (var r in wrong.Take(15))
            Console.WriteLine($"    {r.Name}  ->  {r.Outcome}\n      returned {string.Join(", ", r.Returned)}");
    }

    var scored = results.Count(r => !r.Outcome.StartsWith("SKIP"));
    var hits = results.Count(r => r.Outcome == "HIT");
    Console.WriteLine();
    Console.WriteLine($"Hit rate: {hits}/{scored} ({(scored == 0 ? 0 : 100.0 * hits / scored):F1}%)");
}

// Templates like "<base>/cfg/../*.json" trim to a path containing "..", so both sides must be
// collapsed before comparison or an identical directory reads as a mismatch.
static string Norm(string p)
{
    try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar); }
    catch { return p; }
}

static IEnumerable<string> Tokens(string template) =>
    System.Text.RegularExpressions.Regex.Matches(template, "<[a-zA-Z]+>").Select(m => m.Value);

static Dictionary<string, string> ParseOpts(IEnumerable<string> rest)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var list = rest.ToList();
    for (var i = 0; i < list.Count; i++)
        if (list[i].StartsWith("--") && i + 1 < list.Count)
            d[list[i][2..]] = list[++i];
    return d;
}

internal sealed record Result(string Name, string Outcome, List<string> Expected, List<string> Returned);
