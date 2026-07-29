using SaveLocker.Server.Data;
using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>Maps server entities to wire DTOs.</summary>
public static class Mapping
{
    public static MachineDto ToDto(this Machine m) =>
        new(m.Id, m.Name, m.CreatedAt, m.LastSeen);

    public static GameDto ToDto(this Game g) =>
        new(g.Id, g.Name, g.ManifestKey, g.CustomPathsJson, g.Enabled, g.SuggestedSaveDir,
            null, g.GridUrl, g.HeroUrl, g.LogoUrl, g.IconUrl, g.RetainVersions,
            GlobConfig.Parse(g.ExcludeGlobs), g.ConflictPolicy, g.PreferredMachineId);

    public static GameDto ToDtoWithPath(this Game g, string? machineSavePath) =>
        new(g.Id, g.Name, g.ManifestKey, g.CustomPathsJson, g.Enabled, g.SuggestedSaveDir,
            machineSavePath, g.GridUrl, g.HeroUrl, g.LogoUrl, g.IconUrl, g.RetainVersions,
            GlobConfig.Parse(g.ExcludeGlobs), g.ConflictPolicy, g.PreferredMachineId);

    public static SaveVersionDto ToDto(this SaveVersion v) =>
        new(v.Id, v.GameId, v.MachineId, UploaderName(v), v.CreatedAt,
            v.ContentHash, v.Size, v.ParentVersionId, v.Protected);

    /// <summary>
    /// Names the uploader honestly. A version whose machine has been deleted keeps its snapshotted
    /// name and says so, rather than rendering as an empty string.
    /// </summary>
    private static string UploaderName(SaveVersion v)
    {
        if (v.MachineId is not null) return v.Machine?.Name ?? v.MachineName;
        return string.IsNullOrWhiteSpace(v.MachineName)
            ? "(deleted machine)"
            : $"{v.MachineName} (deleted)";
    }

    public static LeaseDto ToDto(this Lease? lease, Guid gameId) =>
        lease is null
            ? new LeaseDto(gameId, null, null, null, null)
            : new LeaseDto(gameId, lease.MachineId, lease.Machine?.Name,
                lease.AcquiredAt, lease.ExpiresAt);

    public static AgentCommandDto ToDto(this AgentCommand c) =>
        new(c.Id, c.MachineId, c.Machine?.Name, c.GameId, c.Type, c.Force,
            c.Status, c.CreatedAt, c.CompletedAt, c.Result, c.ClaimCount, c.LeaseExpiresAt);

    public static ConflictDto ToDto(this ConflictFlag c, bool escalated = false) =>
        new(c.Id, c.GameId, c.VersionAId, c.VersionBId, c.Status, c.CreatedAt,
            c.ResolvedVersionId, c.ResolvedBy, c.ResolvedAt,
            c.MachineId, c.Count, c.LastSeen, escalated);
}
