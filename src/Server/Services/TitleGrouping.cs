using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SaveLocker.Server.Data;
using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>
/// Groups the games an emulated title is saved as — one per emulator — under one <see cref="GameTitle"/>
/// (tasks/multi-emulator-games). Only ever sets <see cref="Game.TitleId"/>: it never renames a game, moves a version or
/// touches sync, so each emulator's saves stay apart as Decisions.md (2026-10-07) requires.
/// </summary>
public sealed partial class TitleGrouping(AppDbContext db)
{
    private sealed record Info(Game Game, string Emulator, string? Platform, TitleNames.Parsed Parsed, DateTime FirstSaved);

    /// <summary>Group one game with its title's other trees, if it has any. Run when a game gets an emulator source.</summary>
    public async Task AssignAsync(Guid gameId)
    {
        var all = await LoadAsync();
        if (all.FirstOrDefault(i => i.Game.Id == gameId) is not { } me) return;
        if (Assign(all, me) is { } title)
        {
            Audit(gameId, "title.group", $"'{me.Game.Name}' grouped under '{title.BaseName}'");
            await db.SaveChangesAsync();
        }
    }

    /// <summary>The one-time fill: every game that can join a title does, oldest first. Idempotent.</summary>
    public async Task BackfillAsync()
    {
        var all = await LoadAsync();
        var grouped = 0;
        foreach (var me in all.OrderBy(i => i.FirstSaved).ThenBy(i => i.Game.Name.Length))
            if (Assign(all, me) is not null) grouped++;
        if (grouped > 0) Audit(null, "title.backfill", $"{grouped} game(s) grouped under titles");
        await db.SaveChangesAsync();
        await TidyAsync();
    }

    /// <summary>
    /// Take a game out of its title by hand. It becomes its own entry again and stays out (<see cref="Game.TitleLocked"/>).
    /// Returns null when done, else why not ("not_found" for a missing game).
    /// </summary>
    public async Task<string?> SplitAsync(Guid gameId, string by)
    {
        var game = await db.Games.FindAsync(gameId);
        if (game is null) return "not_found";
        if (game.TitleId is null) return $"{game.Name} is not grouped with another emulator's game.";
        var title = await db.GameTitles.FindAsync(game.TitleId);
        game.TitleId = null;
        game.TitleEdition = null;
        game.TitleLocked = true;
        Audit(gameId, "title.split", $"'{game.Name}' split out of '{title?.BaseName}' by {by}");
        await db.SaveChangesAsync();
        await TidyAsync();
        return null;
    }

    /// <summary>
    /// Group <paramref name="gameId"/> with <paramref name="intoId"/> by hand: it joins that game's title, or the two make one.
    /// No version moves, so there is nothing to confirm. Returns null when done, else why not.
    /// </summary>
    public async Task<string?> JoinAsync(Guid gameId, Guid intoId, string by)
    {
        if (gameId == intoId) return "A game cannot be grouped with itself.";
        var all = await LoadAsync();
        var me = all.FirstOrDefault(i => i.Game.Id == gameId);
        var into = all.FirstOrDefault(i => i.Game.Id == intoId);
        if (me is null || into is null) return "not_found";
        // Either may be the titled one: a game alone joins the other's title, never pulls it out of its own.
        if (into.Game.TitleId is null && me.Game.TitleId is not null) (me, into) = (into, me);
        if (me.Game.TitleId is { } current && current == into.Game.TitleId) return $"{me.Game.Name} is already in that title.";
        if (!TitleNames.SamePlatform(me.Platform, into.Platform))
            return $"{me.Game.Name} is for {me.Platform} and {into.Game.Name} for {into.Platform}: only one console's games share a title.";

        var title = into.Game.TitleId is { } id ? await db.GameTitles.FindAsync(id) : null;
        if (title is null)
        {
            var lead = new[] { me, into }.OrderBy(i => i.FirstSaved).ThenBy(i => i.Game.Name.Length).First();
            title = NewTitle(lead, [me, into]);
            Join(into, title);
        }
        Join(me, title);
        me.Game.TitleLocked = false;
        Audit(gameId, "title.join", $"'{me.Game.Name}' grouped with '{into.Game.Name}' under '{title.BaseName}' by {by}");
        await db.SaveChangesAsync();
        await TidyAsync();
        return null;
    }

    /// <summary>Same-console emulator games a hand grouping could put with <paramref name="gameId"/>: any not already in its title.</summary>
    public async Task<List<TitleCandidateDto>> CandidatesAsync(Guid gameId)
    {
        var all = await LoadAsync();
        if (all.FirstOrDefault(i => i.Game.Id == gameId) is not { } me) return [];
        return all
            .Where(o => o.Game.Id != gameId && o.Emulator.Length > 0 && o.Game.EmulatorKey is null &&
                        (me.Game.TitleId is null || o.Game.TitleId != me.Game.TitleId) && TitleNames.SamePlatform(me.Platform, o.Platform))
            .OrderBy(o => o.Parsed.BaseName, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Emulator, StringComparer.OrdinalIgnoreCase)
            .Select(o => new TitleCandidateDto(o.Game.Id, o.Game.Name, o.Emulator, o.Platform, o.Game.Region.Length == 0 ? null : o.Game.Region,
                string.Equals(o.Parsed.BaseName, me.Parsed.BaseName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// A title left with one game (the rest deleted, merged or split out) is no title: its last game stands alone again,
    /// unlocked. A title whose lead left takes the next oldest.
    /// </summary>
    public async Task TidyAsync()
    {
        var titles = await db.GameTitles.ToListAsync();
        if (titles.Count == 0) return;
        var all = await LoadAsync();
        foreach (var title in titles)
        {
            var members = all.Where(i => i.Game.TitleId == title.Id).OrderBy(i => i.FirstSaved).ThenBy(i => i.Game.Name.Length).ToList();
            if (members.Count < 2)
            {
                foreach (var m in members) { m.Game.TitleId = null; m.Game.TitleEdition = null; }
                db.GameTitles.Remove(title);
            }
            else if (members.All(m => m.Game.Id != title.LeadGameId))
            {
                title.LeadGameId = members[0].Game.Id;
                title.BaseName = members[0].Parsed.BaseName;
            }
        }
        await db.SaveChangesAsync();
    }

    private GameTitle? Assign(List<Info> all, Info me)
    {
        if (!Groupable(me) || me.Game.TitleId is not null) return null;
        var matches = all.Where(o => o != me && Groupable(o) && Matches(me, o)).ToList();

        foreach (var existing in matches.Where(o => o.Game.TitleId is not null).GroupBy(o => o.Game.TitleId))
        {
            var members = all.Where(o => o.Game.TitleId == existing.Key).ToList();
            if (members.Any(m => !TitleNames.SameRegion(m.Game.Region, me.Game.Region))) continue;
            var title = db.GameTitles.Local.FirstOrDefault(t => t.Id == existing.Key) ?? db.GameTitles.Find(existing.Key);
            if (title is null) continue;
            Join(me, title);
            return title;
        }

        var group = new List<Info> { me };
        foreach (var o in matches.Where(o => o.Game.TitleId is null).OrderBy(o => o.FirstSaved).ThenBy(o => o.Game.Name.Length))
            if (group.All(g => TitleNames.SameRegion(g.Game.Region, o.Game.Region))) group.Add(o);
        if (group.Count < 2) return null;

        var lead = group.OrderBy(i => i.FirstSaved).ThenBy(i => i.Game.Name.Length).First();
        var created = NewTitle(lead, group);
        foreach (var g in group) Join(g, created);
        return created;
    }

    private GameTitle NewTitle(Info lead, IReadOnlyList<Info> members)
    {
        var title = new GameTitle
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            BaseName = lead.Parsed.BaseName,
            PlatformKey = members.Select(m => m.Platform).FirstOrDefault(p => p is not null) ?? "",
            RegionKey = members.Select(m => m.Game.Region).FirstOrDefault(r => r.Length > 0) ?? "",
            LeadGameId = lead.Game.Id,
        };
        db.GameTitles.Add(title);
        return title;
    }

    private static void Join(Info i, GameTitle title)
    {
        i.Game.TitleId = title.Id;
        i.Game.TitleEdition = i.Parsed.Edition;
    }

    private static bool Matches(Info a, Info b) =>
        TitleNames.SamePlatform(a.Platform, b.Platform) &&
        string.Equals(a.Parsed.BaseName, b.Parsed.BaseName, StringComparison.OrdinalIgnoreCase) &&
        TitleNames.SameRegion(a.Game.Region, b.Game.Region);

    // A PC game has no emulator; a Switch title is already one game across Switch emulators (D2); a split game stays out.
    private static bool Groupable(Info i) =>
        i.Emulator.Length > 0 && !i.Game.TitleLocked && i.Game.EmulatorKey is null && !SwitchEmulator().IsMatch(i.Emulator);

    private async Task<List<Info>> LoadAsync()
    {
        var games = await db.Games.ToListAsync();
        var sources = await db.MachineGameSources.Where(s => s.Kind == GameSourceKinds.Emulator && s.Detail != "").ToListAsync();
        var byGame = sources.GroupBy(s => s.GameId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.UpdatedAt).ToList());
        var firstSaved = await db.SaveVersions.GroupBy(v => v.GameId)
            .Select(g => new { g.Key, First = g.Min(v => v.CreatedAt) }).ToDictionaryAsync(x => x.Key, x => x.First);
        return games.Select(g =>
        {
            var src = byGame.GetValueOrDefault(g.Id) ?? [];
            var emulators = src.Select(s => s.Detail.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return new Info(g, string.Join(" + ", emulators),
                TitleNames.Platform(src.Select(s => s.Tags?.Split('\n')), Mapping.ReadRelease(g)?.Console),
                TitleNames.Parse(g.Name, emulators, g.Region), firstSaved.GetValueOrDefault(g.Id, DateTime.MaxValue));
        }).ToList();
    }

    private void Audit(Guid? gameId, string action, string detail) =>
        db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, GameId = gameId, Action = action, Detail = detail });

    [GeneratedRegex("^(eden|citron|yuzu|suyu|sudachi|ryujinx)$", RegexOptions.IgnoreCase)]
    private static partial Regex SwitchEmulator();
}
