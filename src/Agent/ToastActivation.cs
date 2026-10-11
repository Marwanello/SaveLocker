using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SaveLocker.Agent;

/// <summary>
/// Windows toast activation: the piece that lets a toast's button <b>do</b> something instead of only
/// opening a page (tasks/notification-follow-ups). A notice carrying an <see cref="AgentCommand"/>
/// renders an action with no <c>activationType</c> — "foreground" — and Windows resolves that through
/// this CLSID. While the tray runs, the click is delivered straight to it (the class object below);
/// when it does not, DCOM starts the exe with <c>-ToastActivated</c> and
/// <see cref="ToastActivationHost"/> gets the click to a tray that can perform it.
/// <para>
/// <b>Removable at runtime, unlike a URL scheme.</b> Registration is per-user and goes away with the
/// registration itself: <c>HKCU\Software\Classes\AppUserModelId\{aumid}</c> gains a
/// <c>CustomActivator</c> pointing at <c>HKCU\Software\Classes\CLSID\{clsid}</c>, whose
/// <c>LocalServer32</c> is this exe (the same three keys the Community Toolkit's
/// <c>DesktopNotificationManagerCompat</c> writes for unpackaged desktop apps). The Start-menu
/// shortcut is <i>not</i> what carries activation — the installer's shortcut property is belt and
/// suspenders for the header name, and a rig needs no shortcut at all; measured 2026-10-11 on
/// Windows 11 25H2. This is a different mechanism from the URL-scheme route Group 7 built and
/// rejected (<see cref="NoticeAction"/>): nothing here is aimed at by a browser, and only the CLSID
/// this process registered is ever CoCreated.
/// </para>
/// <para>
/// <b>A rig gets its own CLSID and its own server command line.</b> Both CLSID and identity are
/// derived from the port (<see cref="ClsidForPort"/>, <see cref="ToastPresenter.AumidForPort"/>), so
/// a test tray can never overwrite — or be activated in place of — the installed agent's. A rig's
/// <c>LocalServer32</c> additionally carries <c>--config &lt;path&gt;</c>: a DCOM launch inherits no
/// environment, so without it a click while the test tray is down would start (and act on) the
/// installed agent.
/// </para>
/// </summary>
internal static class ToastActivation
{
    /// <summary>The installed agent's activator CLSID. Also stamped on the Start-menu shortcut by the
    /// installer; <c>run-appearance-consistency-tests</c> fails if the two drift.</summary>
    public static readonly Guid ProductClsid = new("02C97085-6FAE-4DF4-8C52-2E05604D3AB0");

    private static readonly Guid IidNotificationActivationCallback = new("53E31837-6600-4A81-9395-75CFFE746F94");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;
    private const uint COINIT_MULTITHREADED = 0x0;
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);

    /// <summary>The toast identity a click reports, and the CLSID it activates: the product's for
    /// :5178, a deterministic pair of its own for a rig. The last two bytes of the product CLSID are
    /// the port — distinct ports are distinct classes, and no rig can collide with the product.</summary>
    public static Guid ClsidForPort(int port)
    {
        if (port == ToastPresenter.DefaultPort) return ProductClsid;
        var bytes = ProductClsid.ToByteArray();
        bytes[0] = (byte)(port & 0xFF);
        bytes[1] = (byte)((port >> 8) & 0xFF);
        return new Guid(bytes);
    }

    public static string ClsidText(Guid clsid) => "{" + clsid.ToString("D").ToUpperInvariant() + "}";

    /// <summary>The exe DCOM should launch — beside the assembly for both the apphost and a
    /// <c>dotnet &lt;dll&gt;</c> rig run, where <see cref="Environment.ProcessPath"/> would be dotnet.exe.</summary>
    public static string HostExePath() => Path.Combine(AppContext.BaseDirectory, "SaveLocker.Agent.exe");

    /// <summary>
    /// Register this identity and start serving activations. Returns the registration to dispose with
    /// the process. Never throws: a machine that refuses the registry (policy, a locked hive) loses a
    /// working button, not the tray.
    /// </summary>
    public static IDisposable Enable(int port, string aumid, string configPath, Action<string, string> handler)
    {
        var clsid = ClsidForPort(port);
        try
        {
            // A DCOM launch inherits no environment, so any identity that is NOT the OS-default
            // install MUST carry its config on the server's command line. Compared against the
            // env-free default on purpose: comparing with AgentConfig.DefaultConfigPath here would
            // compare the tray's own SAVELOCKER_STATE_ROOT against itself and omit the argument —
            // which is exactly how a rig's click once started the INSTALLED agent (measured
            // 2026-10-11; the host then loaded %PROGRAMDATA%'s config).
            var serverArgs = "-ToastActivated";
            var envFreeDefault = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SaveLocker", "config.json");
            if (!string.Equals(Path.GetFullPath(configPath),
                               Path.GetFullPath(envFreeDefault),
                               StringComparison.OrdinalIgnoreCase))
                serverArgs += $" --config \"{configPath}\"";

            Register(aumid, clsid, HostExePath(), serverArgs);
            AgentLogger.Log($"toast activation: {aumid} → {ClsidText(clsid)}");
        }
        catch (Exception ex) { AgentLogger.LogException("ToastActivation.Register", ex); }

        return Serve(clsid, handler);
    }

    private static void Register(string aumid, Guid clsid, string exePath, string serverArgs)
    {
        using var clsidKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{ClsidText(clsid)}");
        clsidKey.SetValue(null, "SaveLocker toast activation");
        using var serverKey = clsidKey.CreateSubKey("LocalServer32");
        serverKey.SetValue(null, $"\"{exePath}\" {serverArgs}");

        using var aumidKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{aumid}");
        aumidKey.SetValue("CustomActivator", ClsidText(clsid));
    }

    /// <summary>
    /// Every identity of ours that is registered to this exe — what a DCOM-launched host serves. The
    /// launch names no CLSID, so the host registers each one instead: whichever the click was for is
    /// then answerable, including a rig's beside the product's.
    /// </summary>
    public static IEnumerable<(string Aumid, Guid Clsid)> OwnedClsids(string exePath)
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId");
        if (root is null) yield break;

        foreach (var aumid in root.GetSubKeyNames())
        {
            if (!aumid.StartsWith(ToastPresenter.ProductAumid, StringComparison.Ordinal)) continue;
            using var aumidKey = root.OpenSubKey(aumid);
            if (aumidKey?.GetValue("CustomActivator") is not string text || !Guid.TryParse(text, out var clsid))
                continue;
            using var server = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\CLSID\{ClsidText(clsid)}\LocalServer32");
            if (server?.GetValue(null) is string command &&
                command.Contains(exePath, StringComparison.OrdinalIgnoreCase))
                yield return (aumid, clsid);
        }
    }

    /// <summary>
    /// Serve <paramref name="clsid"/> for as long as the returned registration lives. Registered from
    /// a dedicated MTA thread with <c>REGCLS_MULTIPLEUSE</c>: the call arrives on an RPC thread and
    /// never needs the (possibly parked) UI message loop, and the handler is free to post the click
    /// wherever it belongs.
    /// </summary>
    public static IDisposable Serve(Guid clsid, Action<string, string> handler)
    {
        var ready = new ManualResetEventSlim();
        var stop = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            uint cookie = 0;
            var hr = CoRegisterClassObject(ref clsid, new ActivationClassFactory(handler),
                CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out cookie);
            if (hr != 0) AgentLogger.Log($"toast activation: CoRegisterClassObject({ClsidText(clsid)}) failed 0x{hr:X8}");
            ready.Set();
            stop.Wait();
            if (hr == 0) CoRevokeClassObject(cookie);
            CoUninitialize();
        })
        { IsBackground = true, Name = "SaveLocker toast activator" };
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        return new Registration(stop);
    }

    private sealed class Registration(ManualResetEventSlim stop) : IDisposable
    {
        public void Dispose() => stop.Set();
    }

    // ─── COM surface ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotificationUserInput
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Key;
        [MarshalAs(UnmanagedType.LPWStr)] public string Value;
    }

    [ComImport]
    [Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface INotificationActivationCallback
    {
        void Activate(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] NotificationUserInput[] data,
            uint count);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class ActivationCallback(Action<string, string> handler) : INotificationActivationCallback
    {
        public void Activate(string appUserModelId, string invokedArgs, NotificationUserInput[] data, uint count)
        {
            try { handler(appUserModelId ?? "", invokedArgs ?? ""); }
            catch (Exception ex) { AgentLogger.LogException("ToastActivation.Activate", ex); }
        }
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer(bool fLock);
    }

    private sealed class ActivationClassFactory(Action<string, string> handler) : IClassFactory
    {
        private readonly ActivationCallback _callback = new(handler);

        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero) return CLASS_E_NOAGGREGATION;
            // The shell asks for the callback interface; the toolkit (proven on real machines) also
            // answers its class GUID and IUnknown with this same pointer, so both are accepted here.
            if (riid != IidNotificationActivationCallback && riid != IidUnknown) return E_NOINTERFACE;
            ppvObject = Marshal.GetComInterfaceForObject(_callback, typeof(INotificationActivationCallback));
            return 0;
        }

        public int LockServer(bool fLock) => 0;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.Interface)] IClassFactory pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
