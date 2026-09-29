using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// What this machine runs, as the heartbeat reports it (<see cref="AgentOsInfo"/>). Read once per
/// process: an OS upgrade restarts the agent anyway.
/// </summary>
public static class OsIdentity
{
    private static readonly Lazy<AgentOsInfo> Current = new(Detect);

    public static AgentOsInfo This => Current.Value;

    private static AgentOsInfo Detect()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ForWindows(Environment.OSVersion.Version);
            if (OperatingSystem.IsLinux())
            {
                var text = ReadFirst("/etc/os-release", "/usr/lib/os-release");
                return FromOsRelease(
                    text is null ? new Dictionary<string, string>() : ParseOsRelease(text),
                    sysVendor: ReadFirst("/sys/devices/virtual/dmi/id/sys_vendor")?.Trim(),
                    productName: ReadFirst("/sys/devices/virtual/dmi/id/product_name")?.Trim(),
                    wsl: IsWsl());
            }
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("OsIdentity.Detect", ex);
        }
        return new AgentOsInfo("unknown", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    }

    /// <summary>Windows 11 kept major version 10; build 22000 is where it starts.</summary>
    public static AgentOsInfo ForWindows(Version v)
    {
        var name = v.Major == 10
            ? (v.Build >= 22000 ? "Windows 11" : "Windows 10")
            : $"Windows {v.Major}.{v.Minor}";
        return new AgentOsInfo("windows", $"{name} (build {v.Build})");
    }

    /// <summary>
    /// <c>ID</c> lowercased as the spec requires; <c>PRETTY_NAME</c> falls back to <c>NAME</c> and then
    /// to the id. A file with no <c>ID</c> means plain "linux", which is the spec's own default.
    /// </summary>
    public static AgentOsInfo FromOsRelease(IReadOnlyDictionary<string, string> kv, string? sysVendor, string? productName, bool wsl)
    {
        string? Get(string key) => kv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var id = Get("ID")?.ToLowerInvariant() ?? "linux";
        var name = Get("PRETTY_NAME") ?? Get("NAME") ?? id;
        return new AgentOsInfo(
            Id: id,
            Name: name,
            IdLike: Get("ID_LIKE")?.ToLowerInvariant(),
            VariantId: Get("VARIANT_ID")?.ToLowerInvariant(),
            Device: wsl ? "WSL" : DeviceName(sysVendor, productName));
    }

    /// <summary>Only hardware worth naming. A generic board's DMI strings are mostly "To Be Filled By O.E.M.".</summary>
    public static string? DeviceName(string? sysVendor, string? productName)
    {
        if (!string.Equals(sysVendor, "Valve", StringComparison.OrdinalIgnoreCase)) return null;
        return productName switch
        {
            "Jupiter" => "Steam Deck",
            "Galileo" => "Steam Deck OLED",
            _ => "Valve hardware",
        };
    }

    /// <summary>
    /// os-release(5): <c>KEY=value</c> lines, the value optionally in single or double quotes with
    /// backslash escapes inside double quotes; blank lines and <c>#</c> comments ignored.
    /// </summary>
    public static Dictionary<string, string> ParseOsRelease(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                var inner = value[1..^1];
                var sb = new System.Text.StringBuilder(inner.Length);
                for (var i = 0; i < inner.Length; i++)
                {
                    if (inner[i] == '\\' && i + 1 < inner.Length) i++;
                    sb.Append(inner[i]);
                }
                value = sb.ToString();
            }
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1];
            }
            result[key] = value;
        }
        return result;
    }

    private static bool IsWsl()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"))) return true;
        var release = ReadFirst("/proc/sys/kernel/osrelease");
        return release is not null && release.Contains("microsoft", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadFirst(params string[] paths)
    {
        foreach (var p in paths)
        {
            try { if (File.Exists(p)) return File.ReadAllText(p); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
}
