using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace SaveLocker.Agent;

/// <summary>
/// "Is the process on the other end of this loopback connection running as the same OS account as us?"
/// <para>
/// The agent UI's page carries the local API token (<see cref="LocalAuth"/>). The Host and Origin checks keep web
/// pages away from it, but loopback is shared by every account on the machine: without this, another local user
/// could fetch <c>/</c> and read the token the per-account ACL on <c>api-token</c> exists to keep from them. The
/// answer comes from the OS's own connection table, which a client cannot forge: <c>/proc/net/tcp{,6}</c>'s uid
/// column on Linux, and on Windows the owning process of the connection (<c>GetExtendedTcpTable</c>) and its
/// token's user SID. Anything that cannot be established is "no".
/// </para>
/// </summary>
public static class LoopbackPeer
{
    /// <param name="clientPort">The connection's remote port, as the server sees it.</param>
    /// <param name="serverPort">The port the server accepted it on.</param>
    public static bool IsSameUser(IPAddress? clientAddress, int clientPort, int serverPort)
    {
        if (clientAddress is null || !IPAddress.IsLoopback(clientAddress)) return false;
        try
        {
            if (OperatingSystem.IsLinux()) return IsSameUserLinux(clientPort, serverPort);
            if (OperatingSystem.IsWindows()) return IsSameUserWindows(clientPort, serverPort);
        }
        catch (Exception ex) { AgentLogger.Log($"LoopbackPeer: could not identify the caller: {ex.Message}"); }
        return false;
    }

    // ----- Linux -----

    private static bool IsSameUserLinux(int clientPort, int serverPort)
    {
        if (EffectiveUid() is not { } me) return false;
        foreach (var table in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            if (!File.Exists(table)) continue;
            if (FindSocketUid(File.ReadAllText(table), clientPort, serverPort) is { } uid) return uid == me;
        }
        return false;
    }

    /// <summary>
    /// The uid owning the client end of a connection in a <c>/proc/net/tcp</c>-format table: the row whose local
    /// port is <paramref name="clientPort"/> and remote port <paramref name="serverPort"/>. Null when there is none.
    /// </summary>
    public static int? FindSocketUid(string table, int clientPort, int serverPort)
    {
        foreach (var line in table.Split('\n').Skip(1))
        {
            // sl local_address rem_address st tx_queue:rx_queue tr:tm->when retrnsmt uid ...
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 8) continue;
            if (Port(f[1]) == clientPort && Port(f[2]) == serverPort &&
                int.TryParse(f[7], NumberStyles.None, CultureInfo.InvariantCulture, out var uid))
                return uid;
        }
        return null;

        static int? Port(string addressAndPort) =>
            addressAndPort.LastIndexOf(':') is var i and >= 0 &&
            int.TryParse(addressAndPort[(i + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var p)
                ? p : null;
    }

    private static int? EffectiveUid()
    {
        // "Uid:\treal\teffective\tsaved\tfs"
        foreach (var line in File.ReadLines("/proc/self/status"))
            if (line.StartsWith("Uid:", StringComparison.Ordinal))
            {
                var f = line[4..].Split('\t', StringSplitOptions.RemoveEmptyEntries);
                return f.Length > 1 && int.TryParse(f[1], out var euid) ? euid : null;
            }
        return null;
    }

    // ----- Windows -----

    [SupportedOSPlatform("windows")]
    private static bool IsSameUserWindows(int clientPort, int serverPort)
    {
        if (OwningPid(clientPort, serverPort) is not { } pid) return false;
        if (pid == Environment.ProcessId) return true;

        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return false; // another account's process: its DACL does not let us in
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) return false;
            try
            {
                GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var size);
                if (size == 0) return false;
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, TokenUser, buffer, size, out _)) return false;
                    var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)); // TOKEN_USER.User.Sid
                    using var me = WindowsIdentity.GetCurrent();
                    return sid.Equals(me.User);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    [SupportedOSPlatform("windows")]
    private static int? OwningPid(int clientPort, int serverPort) =>
        OwningPid(AfInet, rowSize: 24, localPortAt: 8, remotePortAt: 16, pidAt: 20, clientPort, serverPort) ??
        OwningPid(AfInet6, rowSize: 56, localPortAt: 20, remotePortAt: 44, pidAt: 52, clientPort, serverPort);

    /// <summary>
    /// Walks a MIB_TCPTABLE_OWNER_PID (IPv4, 24-byte rows) or MIB_TCP6TABLE_OWNER_PID (IPv6, 56-byte rows) for the
    /// client end of the connection. Ports sit in the low 16 bits of a DWORD, in network byte order.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int? OwningPid(int family, int rowSize, int localPortAt, int remotePortAt, int pidAt,
        int clientPort, int serverPort)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidConnections, 0);
        for (var attempt = 0; attempt < 3 && size > 0; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var rc = GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidConnections, 0);
                if (rc == ErrorInsufficientBuffer) continue;
                if (rc != 0) return null;
                var count = Marshal.ReadInt32(buffer);
                for (var i = 0; i < count; i++)
                {
                    var row = buffer + 4 + i * rowSize;
                    if (NetPort(Marshal.ReadInt32(row + localPortAt)) == clientPort &&
                        NetPort(Marshal.ReadInt32(row + remotePortAt)) == serverPort)
                        return Marshal.ReadInt32(row + pidAt);
                }
                return null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;

        static int NetPort(int dword) => ((dword & 0xFF) << 8) | ((dword >> 8) & 0xFF);
    }

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidConnections = 4;
    private const int ErrorInsufficientBuffer = 122;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, int reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
