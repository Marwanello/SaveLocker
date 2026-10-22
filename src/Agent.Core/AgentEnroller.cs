using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>What one join did: the identity it bought and how many games pre-seeded.</summary>
public sealed record JoinOutcome(string MachineName, Guid MachineId, int Mapped, int Unmapped);

/// <summary>
/// One implementation of "join a server", shared by the CLI (<c>enroll --url</c> / <c>enroll --code</c>)
/// and the daemon's local API (<c>POST /api/join</c>, which is what the agent UI's Settings tab and a
/// <c>#enroll:</c> deep link call). Two copies of this would drift the moment one of them learned a
/// new field of the policy.
/// </summary>
public static class AgentEnroller
{
    /// <summary>
    /// The absolute URL of the policy behind a join link. The link itself is what a person has
    /// (<c>/join/K7QM-4H2X</c>); the policy is its sibling. A URL that already ends in
    /// <c>/policy</c> is taken as-is, so a saved policy URL keeps working too.
    /// </summary>
    public static string PolicyUrlFor(string joinUrl)
    {
        var uri = new Uri(joinUrl.Trim(), UriKind.Absolute);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/policy", StringComparison.Ordinal)) path += "/policy";
        return $"{uri.GetLeftPart(UriPartial.Authority)}{path}";
    }

    /// <summary>
    /// Fetch the policy a join link points at. Built fresh by the server at this moment, so its
    /// game list describes the server as it is now — not as it was when the link was minted.
    /// </summary>
    public static async Task<EnrollmentPolicy> FetchPolicyAsync(string joinUrl)
    {
        var uri = new Uri(joinUrl.Trim(), UriKind.Absolute);
        var api = new ApiClient(uri.GetLeftPart(UriPartial.Authority), apiKey: null);
        return await api.FetchPolicyAsync(PolicyUrlFor(joinUrl));
    }

    /// <summary>
    /// Apply a fetched policy: validate it, trade its code for this machine's real API key, record
    /// the TOFU pin, and pre-seed the games so the agent is useful before its first poll rather
    /// than twenty seconds after it.
    /// </summary>
    /// <param name="nameOverride">The CLI's <c>--name</c> (or the Settings field), sent as the
    /// redeem's <c>machineName</c>. A code minted for a machine name is BINDING and wins: the
    /// SERVER rebinds the machine to the code's name and returns it, which is what stops a leaked
    /// link from claiming another machine's identity.</param>
    /// <param name="log">Everything the old <c>enroll</c> printed; the CLI sends it to the console
    /// and the log, the daemon to the log.</param>
    public static async Task<JoinOutcome> ApplyAsync(
        AgentConfig config, EnrollmentPolicy policy, Detection detection, string? nameOverride, Action<string> log)
    {
        if (policy.Version > EnrollmentPolicy.CurrentVersion)
            throw new InvalidOperationException(
                $"This join link is version {policy.Version}; this agent understands up to " +
                $"{EnrollmentPolicy.CurrentVersion}. Update the agent.");
        if (string.IsNullOrWhiteSpace(policy.ServerUrl) || string.IsNullOrWhiteSpace(policy.Token))
            throw new InvalidOperationException("The join link is missing the server URL or the code.");

        // Expiry is enforced by the server; checking it here only buys a clearer error than a 404.
        if (policy.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException(
                $"This join link expired at {policy.ExpiresAt:u}. Create a new one from the console.");

        // Validated before anything is written, and this is also what drops the key/id/pin of a
        // DIFFERENT server being replaced — the same rule the Settings tab's Save follows.
        if (!config.TrySetServerUrl(policy.ServerUrl, out _))
            throw new InvalidOperationException(ServerOrigin.InvalidUrlMessage);
        var serverUrl = config.ServerUrl;

        if (!ServerTrust.IsPinnable(serverUrl))
            log($"Note: {serverUrl} is plain http. The code is sent in the clear and the server's " +
                "identity cannot be pinned — fine on a trusted LAN, not over the internet.");

        // No pin yet, by definition: this IS the first use that trust-on-first-use trusts.
        var api = new ApiClient(serverUrl, apiKey: null);
        var name = nameOverride ?? policy.MachineName ?? config.MachineName;
        var reg = await api.EnrollAsync(policy.Token, name);

        config.MachineName = reg.MachineName; // the server decides: a code minted for a machine binds the name
        config.ApiKey = reg.ApiKey;
        config.MachineId = reg.MachineId;
        if (policy.SettleQuietSeconds is { } quiet) config.SettleQuietSeconds = quiet;
        if (policy.SettleMaxWaitSeconds is { } max) config.SettleMaxWaitSeconds = max;
        config.SetPin(api.Observed);

        log($"Enrolled '{reg.MachineName}' (id {reg.MachineId}) with {serverUrl}.");
        if (config.ServerPin is not null)
            log($"Pinned the server's TLS key: {config.ServerPin} ({ServerTrust.Describe(config)})");

        // From here on the agent has an identity and a pin, so it talks to the server as itself —
        // the enroll client above was built before either existed and carries no key.
        var (mapped, unmapped) = await SeedGamesAsync(policy, config, detection, ApiClient.For(config), log);
        config.Save();

        return new JoinOutcome(reg.MachineName, reg.MachineId, mapped, unmapped);
    }

    /// <summary>
    /// Pre-seed the policy's games. The server stays authoritative — the poller's reconcile would
    /// adopt these anyway and will correct anything set here — so this is a head start, not a second
    /// source of truth. Save folders are resolved on THIS machine; the console cannot know them.
    /// </summary>
    private static async Task<(int mapped, int unmapped)> SeedGamesAsync(
        EnrollmentPolicy policy, AgentConfig config, Detection detection, ApiClient api, Action<string> log)
    {
        var mapped = 0;
        var unmapped = 0;

        foreach (var pg in policy.Games ?? Array.Empty<EnrollmentGame>())
        {
            var dir = await ResolveForEnrollAsync(pg, detection);

            // One copy-on-write pass (MutateGames) for both the add and the update: the live list is
            // never mutated in place, the same rule every other writer in AgentConfig follows — a
            // reader enumerating it (the Deck UI's render loop) must never see it change underfoot.
            config.MutateGames(list =>
            {
                var tracked = list.FirstOrDefault(g => g.GameId == pg.GameId);
                if (tracked is null)
                {
                    tracked = new TrackedGame { GameId = pg.GameId };
                    list.Add(tracked);
                }
                tracked.Name = pg.Name;
                tracked.ManifestKey = pg.ManifestKey;
                tracked.ExcludeGlobs = (pg.ExcludeGlobs ?? Array.Empty<string>()).ToList();
                if (!string.IsNullOrEmpty(dir)) tracked.SaveDirectory = dir;
            });

            var saveDir = config.Games.FirstOrDefault(g => g.GameId == pg.GameId)?.SaveDirectory ?? "";
            if (string.IsNullOrEmpty(saveDir))
            {
                unmapped++;
                log($"  {pg.Name}: no save folder found here — map it with: add-game --name \"{pg.Name}\" --dir <path>");
                continue;
            }

            mapped++;
            log($"  {pg.Name}: {saveDir}");
            // Tell the server where this machine put it, so the console can show it.
            try { await api.SetMachinePathAsync(pg.GameId, saveDir); }
            catch (Exception ex) { AgentLogger.LogException("enroll.SetMachinePath", ex); }
        }

        return (mapped, unmapped);
    }

    /// <summary>The policy's hint if that folder exists here, else Ludusavi detection, else nothing.</summary>
    private static async Task<string?> ResolveForEnrollAsync(EnrollmentGame pg, Detection detection)
    {
        // A template first — enroll is the Deck's whole setup, so if the console describes a save
        // location generically this is the first place that has to understand it. Expanding here is
        // also what keeps a newly enrolled machine's save root at the SAME logical level as every
        // other machine's; adopting another machine's literal path is what makes them diverge.
        if (PathResolver.IsTemplate(pg.SuggestedSaveDir) &&
            Detection.HostResolver()?.ResolveToDirectory(pg.SuggestedSaveDir!) is { } expanded &&
            Directory.Exists(expanded))
            return Path.GetFullPath(expanded);

        if (!string.IsNullOrWhiteSpace(pg.SuggestedSaveDir) &&
            !PathResolver.IsTemplate(pg.SuggestedSaveDir) &&
            Directory.Exists(pg.SuggestedSaveDir))
            return Path.GetFullPath(pg.SuggestedSaveDir);
        try
        {
            var dirs = await detection.ResolveSaveDirectoriesAsync(pg.ManifestKey ?? pg.Name);
            return dirs.FirstOrDefault();
        }
        catch { return null; }
    }
}
