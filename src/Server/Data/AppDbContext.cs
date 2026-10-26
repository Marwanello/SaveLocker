using Microsoft.EntityFrameworkCore;

namespace SaveLocker.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameTitle> GameTitles => Set<GameTitle>();
    public DbSet<Playthrough> Playthroughs => Set<Playthrough>();
    public DbSet<DeviceSelection> DeviceSelections => Set<DeviceSelection>();
    public DbSet<SaveVersion> SaveVersions => Set<SaveVersion>();
    public DbSet<SaveVersionFile> SaveVersionFiles => Set<SaveVersionFile>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<ConflictFlag> Conflicts => Set<ConflictFlag>();
    public DbSet<AgentCommand> AgentCommands => Set<AgentCommand>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<MachineSavePath> MachineSavePaths => Set<MachineSavePath>();
    public DbSet<MachineGameContent> MachineGameContents => Set<MachineGameContent>();
    public DbSet<ContentTransfer> ContentTransfers => Set<ContentTransfer>();
    public DbSet<GameSavePath> GameSavePaths => Set<GameSavePath>();
    public DbSet<MachineScanCandidate> MachineScanCandidates => Set<MachineScanCandidate>();
    public DbSet<MachineGameSource> MachineGameSources => Set<MachineGameSource>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<AgentHealth> AgentHealth => Set<AgentHealth>();
    public DbSet<AgentEvent> AgentEvents => Set<AgentEvent>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Machine>().HasIndex(m => m.Name).IsUnique();
        // Two releases of one game share a name; one region's release is one game.
        b.Entity<Game>().HasIndex(g => new { g.Name, g.Region }).IsUnique();
        // D2: one game per emulated title.
        b.Entity<Game>().HasIndex(g => g.EmulatorKey).IsUnique().HasFilter("\"EmulatorKey\" IS NOT NULL");
        // A title is a console-side group: deleting it leaves its games standing alone, never deletes them.
        b.Entity<Game>().HasIndex(g => g.TitleId);
        b.Entity<Game>().HasOne(g => g.Title).WithMany().HasForeignKey(g => g.TitleId).OnDelete(DeleteBehavior.SetNull);

        // One active lease per playthrough of a game: two devices may play two playthroughs at once.
        b.Entity<Lease>().HasIndex(l => new { l.GameId, l.PlaythroughId }).IsUnique();

        // A game's playthroughs (tasks/save-playthroughs). A name is unique within the game ignoring case (NOCASE, so the
        // database refuses the second "hard mode" a racing request would otherwise slip in), and exactly one is the default.
        b.Entity<Playthrough>().Property(p => p.Name).UseCollation("NOCASE");
        b.Entity<Playthrough>().HasIndex(p => new { p.GameId, p.Name }).IsUnique();
        b.Entity<Playthrough>().HasIndex(p => p.GameId).IsUnique().HasFilter("\"IsDefault\" = 1")
            .HasDatabaseName("IX_Playthroughs_GameId_Default");
        b.Entity<Playthrough>()
            .HasOne(p => p.Game).WithMany()
            .HasForeignKey(p => p.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // One selection per device per game. Both sides cascade: a selection means nothing once its machine, game or
        // playthrough is gone.
        b.Entity<DeviceSelection>().HasKey(d => new { d.MachineId, d.GameId });
        b.Entity<DeviceSelection>().HasIndex(d => d.PlaythroughId);
        b.Entity<DeviceSelection>()
            .HasOne<Machine>().WithMany()
            .HasForeignKey(d => d.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeviceSelection>()
            .HasOne<Game>().WithMany()
            .HasForeignKey(d => d.GameId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeviceSelection>()
            .HasOne<Playthrough>().WithMany()
            .HasForeignKey(d => d.PlaythroughId)
            .OnDelete(DeleteBehavior.Cascade);

        // Versions, conflicts and leases belong to a playthrough. No foreign key: the row that matters (the version) is
        // already tied to its game, and a restricted key here would make deleting a game depend on the order EF removes
        // its children in.
        b.Entity<SaveVersion>().HasIndex(v => new { v.PlaythroughId, v.CreatedAt });
        b.Entity<ConflictFlag>().HasIndex(c => new { c.PlaythroughId, c.Status });

        b.Entity<SaveVersion>().HasIndex(v => new { v.GameId, v.CreatedAt });
        // Save history outlives the machine that produced it: deleting a machine must null the
        // link, never cascade. SaveVersion.MachineName keeps the uploader nameable afterwards.
        b.Entity<SaveVersion>()
            .HasOne(v => v.Machine).WithMany()
            .HasForeignKey(v => v.MachineId)
            .OnDelete(DeleteBehavior.SetNull);

        // One manifest row per (version, path); the lookup is always "give me this version's whole
        // manifest" or "does this version have one at all", both served by the PK/FK index alone.
        // Cascades with the version: a pruned/deleted SaveVersion's per-file baseline is meaningless
        // once the archive it describes is gone.
        b.Entity<SaveVersionFile>().HasKey(f => new { f.VersionId, f.Path });
        b.Entity<SaveVersionFile>()
            .HasOne(f => f.Version).WithMany()
            .HasForeignKey(f => f.VersionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ConflictFlag>().HasIndex(c => new { c.GameId, c.Status });
        b.Entity<AuditLog>().HasIndex(a => a.Timestamp);
        b.Entity<AgentCommand>().HasIndex(c => new { c.MachineId, c.Status });

        b.Entity<AppSetting>().HasKey(s => s.Key);

        // One row per machine per save folder of a game. Both sides cascade: a path is meaningless
        // once its machine or its game is gone (the hand-written RemoveRange calls stay as well).
        b.Entity<MachineSavePath>().HasKey(p => new { p.MachineId, p.GameId, p.PathKey });
        b.Entity<MachineSavePath>().Property(p => p.PathKey).HasDefaultValue(SaveLocker.Shared.SaveRoot.PrimaryKey);
        b.Entity<MachineSavePath>()
            .HasOne<Machine>().WithMany()
            .HasForeignKey(p => p.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<MachineSavePath>()
            .HasOne<Game>().WithMany()
            .HasForeignKey(p => p.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ContentTransfer>().HasIndex(t => new { t.GameId, t.ToMachineId });
        b.Entity<ContentTransfer>()
            .HasOne<Game>().WithMany()
            .HasForeignKey(t => t.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<MachineGameContent>().HasKey(c => new { c.MachineId, c.GameId });
        b.Entity<MachineGameContent>()
            .HasOne<Machine>().WithMany()
            .HasForeignKey(c => c.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<MachineGameContent>()
            .HasOne<Game>().WithMany()
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<GameSavePath>().HasKey(p => new { p.GameId, p.Key });
        b.Entity<GameSavePath>().HasQueryFilter(p => p.RetiredAt == null);
        b.Entity<GameSavePath>()
            .HasOne<Game>().WithMany()
            .HasForeignKey(p => p.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same key shape as the confirmed path, so a machine reports at most one guess per game.
        // Both sides cascade: a guess is meaningless once its machine or its game is gone.
        b.Entity<MachineScanCandidate>().HasKey(c => new { c.MachineId, c.GameId });
        b.Entity<MachineScanCandidate>()
            .HasOne<Machine>().WithMany()
            .HasForeignKey(c => c.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<MachineScanCandidate>()
            .HasOne<Game>().WithMany()
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<MachineGameSource>().HasKey(s => new { s.MachineId, s.GameId });
        b.Entity<MachineGameSource>()
            .HasOne<Machine>().WithMany()
            .HasForeignKey(s => s.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<MachineGameSource>()
            .HasOne<Game>().WithMany()
            .HasForeignKey(s => s.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Redeem looks a token up by hash; unique so a hash can never map to two rows.
        b.Entity<EnrollmentToken>().HasIndex(t => t.TokenHash).IsUnique();

        // Every admin request looks its session up by token hash; unique so one hash is one session.
        b.Entity<AdminSession>().HasIndex(t => t.TokenHash).IsUnique();

        // One health row per machine, and it dies with the machine.
        b.Entity<AgentHealth>().HasKey(h => h.MachineId);
        b.Entity<AgentHealth>()
            .HasOne(h => h.Machine).WithOne()
            .HasForeignKey<AgentHealth>(h => h.MachineId)
            .OnDelete(DeleteBehavior.Cascade);

        // The dedupe lookup on every heartbeat: this machine's open event for this code+game.
        b.Entity<AgentEvent>().HasIndex(e => new { e.MachineId, e.Code, e.GameId, e.ResolvedAt });
        b.Entity<AgentEvent>()
            .HasOne(e => e.Machine).WithMany()
            .HasForeignKey(e => e.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
        // A deleted game must not drag its events' machine rows down with it — null the link instead.
        b.Entity<AgentEvent>()
            .HasOne(e => e.Game).WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
