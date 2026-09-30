namespace SaveLocker.Agent.Linux.Art;

/// <summary>The Linux side of <see cref="SteamArt"/>: where the layers and the artwork folder are, and when to repaint.</summary>
public static class SteamArtHost
{
    private static readonly object Gate = new();

    /// <summary>Where install.sh puts the bundled art; XDG_DATA_HOME moves it, as it does the rest of the agent's state.</summary>
    public static string ArtworkDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveLocker", "artwork");

    public static byte[] Layer(string name)
    {
        var resource = "SaveLocker.Agent.Linux.Art.layers." + name;
        using var s = typeof(SteamArtHost).Assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"Art layer '{name}' is not embedded in this build.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Repaint the artwork folder for the look in effect right now. Never throws: art is decoration.</summary>
    public static SteamArt.Outcome Sync(AgentConfig config)
    {
        try
        {
            var look = config.EffectiveAppearance;
            lock (Gate)
            {
                var outcome = SteamArt.Apply(ArtworkDir, look.Accent, look.Mark, Layer);
                if (outcome.Written > 0)
                    AgentLogger.Log($"Steam art: repainted {outcome.Written} picture(s) in {ArtworkDir} " +
                                    $"as {look.Accent}/{look.Mark}. Set them as custom artwork in Steam to use them.");
                return outcome;
            }
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("SteamArtHost.Sync", ex);
            return SteamArt.Outcome.None;
        }
    }

    /// <summary>Paint now, and again whenever the look changes. Rendering is off the caller's thread — the change
    /// arrives on a heartbeat or a request, neither of which should wait on a few hundred milliseconds of pixels.</summary>
    public static void Watch(AgentConfig config)
    {
        _ = Task.Run(() => Sync(config));
        config.AppearanceChanged += _ => Task.Run(() => Sync(config));
    }
}
