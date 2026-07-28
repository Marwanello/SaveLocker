using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SaveLocker.Agent;

/// <summary>
/// Makes the agent's state directory private on Windows.
///
/// <para>
/// <c>%PROGRAMDATA%</c> grants every authenticated user read access, and that inherits. Two of the
/// files below it are not preferences: <c>config.json</c> holds this machine's <b>server API key</b>,
/// and <c>api-token</c> grants the local management API — which can rewrite config, re-register the
/// machine, and enroll games. Any other local user could read both. That is the whole of WA-03.
/// </para>
///
/// <para>
/// The state belongs to <b>one Windows account</b>: whoever enrolled. That follows the shape the
/// installer already chose deliberately — it refuses to create this directory while elevated, so the
/// de-elevated tray user creates it and can rewrite config.json forever after (see
/// <c>installer/SaveLocker.iss</c>). SYSTEM and Administrators are kept because an administrator can
/// take ownership regardless, and removing them only breaks backup and repair without adding any
/// protection. Everyone else is removed, along with the inheritance that let them in.
/// </para>
///
/// <para>
/// Applied to the <i>directory</i> with inheritable rules, so every file the agent later creates —
/// queue, health events, lease warnings, locks, logs, temp archives — is covered without each
/// writer having to remember. Existing files created before this fix keep their old inherited ACL,
/// so <see cref="Protect"/> re-points them at the parent once.
/// </para>
/// </summary>
public static class StateDirSecurity
{
    /// <summary>
    /// Lock down the state directory and everything already in it. Idempotent and cheap on the
    /// common path: an already-protected directory is detected and skipped.
    /// Returns false if the ACL could not be applied — the caller decides how loudly to say so.
    /// </summary>
    public static bool Protect(string stateDir)
    {
        // Linux has its own model, already applied: 0700 on the directory and 0600 on the token.
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            return ProtectWindows(stateDir);
        }
        catch (Exception ex)
        {
            // Never fatal. An agent that refuses to start protects nothing, and the most likely
            // cause is a directory owned by another account — where the honest outcome is a loud
            // log line, not a crash.
            AgentLogger.Log($"WARNING: could not restrict permissions on {stateDir}: {ex.Message}. " +
                            "Other local users may be able to read this machine's API key and local " +
                            "API token.");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool ProtectWindows(string stateDir)
    {
        Directory.CreateDirectory(stateDir);
        var dir = new DirectoryInfo(stateDir);
        var sec = dir.GetAccessControl(AccessControlSections.Access);

        // Already ours: protected means inheritance from ProgramData is severed, which is the one
        // thing that cannot happen by accident. Re-applying on every config load would be pointless
        // I/O on a path that runs constantly.
        if (sec.AreAccessRulesProtected) return true;

        // preserveInheritance:false — drop the inherited rules rather than copying them down.
        // Copying them first and purging afterwards does NOT work: the copies are materialised when
        // the descriptor is persisted, so the explicit-rules collection below never sees them and
        // ProgramData's grant to Authenticated Users survives the purge. Nothing is written until
        // SetAccessControl, so there is no window where the directory has no ACL.
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in
                 sec.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
            sec.RemoveAccessRuleSpecific(rule);

        foreach (var sid in Owners())
            sec.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                // Applies to this folder, its subfolders and its files — so anything created later
                // is born with these permissions rather than acquiring them.
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

        dir.SetAccessControl(sec);
        AgentLogger.Log($"Restricted {stateDir} to this account, SYSTEM and Administrators.");

        ReinheritExistingChildren(dir);
        return true;
    }

    /// <summary>
    /// The accounts allowed to read this machine's credentials.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static IEnumerable<IdentityReference> Owners()
    {
        // The enrolling user. WindowsIdentity.GetCurrent().User is the account the agent actually
        // runs as, which is what has to keep working after a reboot and after a silent update.
        using var me = WindowsIdentity.GetCurrent();
        if (me.User is { } user) yield return user;

        yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
    }

    /// <summary>
    /// Files written before this fix carry their own copy of the old, permissive inherited ACL —
    /// tightening the parent does not touch them. Clearing their explicit rules and re-enabling
    /// inheritance makes them pick up the parent's new rules instead.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void ReinheritExistingChildren(DirectoryInfo dir)
    {
        foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            try
            {
                var fsec = file.GetAccessControl(AccessControlSections.Access);
                foreach (FileSystemAccessRule rule in
                         fsec.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
                    fsec.RemoveAccessRuleSpecific(rule);
                fsec.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                file.SetAccessControl(fsec);
            }
            catch (Exception ex)
            {
                AgentLogger.Log($"could not restrict permissions on {file.FullName}: {ex.Message}");
            }
        }
    }
}
