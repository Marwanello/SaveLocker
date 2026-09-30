namespace SaveLocker.Agent.Linux.Art;

/// <summary>The Linux side of <see cref="SteamArt"/>: where the layers and the artwork folder are, and when to repaint.</summary>
public static class SteamArtHost
{
    private static readonly object Gate = new();

    /// <summary>Where install.sh puts the bundled art: beside this agent's own state. A second agent run with
    /// <c>--config</c>, or the test rig's, has its own folder and never repaints the installed one's pictures.</summary>
    public static string ArtworkDir(AgentConfig config) => Path.Combine(config.StateDir, "artwork");

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
            lock (Gate)
            {
                // Read inside the gate: two changes in quick succession each start a repaint, and the one that
                // read the older look must not be the one that finishes last.
                var look = config.EffectiveAppearance;
                var dir = ArtworkDir(config);
                var outcome = SteamArt.Apply(dir, look.Accent, look.Mark, Layer);
                if (outcome.Written > 0)
                    AgentLogger.Log($"Steam art: repainted {outcome.Written} picture(s) in {dir} as {look.Accent}/{look.Mark}. " +
                                    "Steam keeps its own copy of a picture — set them as custom artwork again to see them there.");
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
