namespace SaveLocker.Agent.Linux.Art;

/// <summary>The Linux side of <see cref="SteamArt"/>: where the layers live, where Steam is, and when to repaint.</summary>
public static class SteamArtHost
{
    private static readonly object Gate = new();

    public static byte[] Layer(string name)
    {
        var resource = "SaveLocker.Agent.Linux.Art.layers." + name;
        using var s = typeof(SteamArtHost).Assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"Art layer '{name}' is not embedded in this build.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Paint the shortcut's art for the look in effect right now. Never throws: art is decoration.</summary>
    public static SteamArt.Outcome Sync(AgentConfig config)
    {
        try
        {
            var look = config.EffectiveAppearance;
            lock (Gate)
            {
                var outcome = SteamArt.Apply(SteamRoots.Find(), look.Accent, look.Mark, Layer);
                if (outcome.Written > 0)
                    AgentLogger.Log($"Steam art: painted {outcome.Written} picture(s) for {outcome.Shortcuts} SaveLocker shortcut(s) " +
                                    $"in {look.Accent}/{look.Mark}. Steam shows them after it restarts.");
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
