using System.Windows.Forms;

namespace SaveLocker.Agent;

/// <summary>
/// The single owning thread for every WinForms object in the tray — the icon, its menu, the
/// WebView2 window, message boxes and the folder dialog.
///
/// <para>
/// It replaces a captured <see cref="SynchronizationContext"/>, which did not work here.
/// <c>TrayContext</c> is constructed as the ARGUMENT to <c>Application.Run</c>, so its constructor
/// ran before the message loop installed <c>WindowsFormsSynchronizationContext</c>; the capture
/// therefore fell through to <c>new SynchronizationContext()</c> and every "marshal to the UI
/// thread" call in the tray was a plain thread-pool post to a random worker. Nothing failed loudly:
/// WinForms only throws on a cross-thread call once a handle exists, and a NotifyIcon's menu is
/// rebuilt far more often than the window is open.
/// </para>
/// <para>
/// Forcing this control's handle is what fixes it at the root: creating a handle is itself what
/// installs the WinForms synchronization context on the creating thread, so the dispatcher does not
/// depend on being constructed at any particular point in the startup sequence — it establishes the
/// owner rather than hoping to find one.
/// </para>
/// </summary>
internal sealed class UiDispatcher : IDisposable
{
    private readonly Control _marshal;
    private readonly int _threadId;
    private volatile bool _shutdown;

    /// <summary>Must be constructed on the thread that will run the WinForms message loop.</summary>
    public UiDispatcher()
    {
        _marshal = new Control();
        _ = _marshal.Handle;
        _threadId = Environment.CurrentManagedThreadId;
        Owner = $"thread {_threadId}, context " +
                (SynchronizationContext.Current?.GetType().Name ?? "none");
    }

    /// <summary>
    /// Who owns the WinForms objects, for the log. Worth a line on every start: the defect this
    /// class fixes was invisible precisely because the wrong context still accepted every Post.
    /// </summary>
    public string Owner { get; }

    /// <summary>True when the caller is already the owning thread.</summary>
    public bool IsOwner => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>
    /// Queue work on the owning thread and return immediately. Safe before the loop starts — the
    /// call sits in the window's message queue until <c>Application.Run</c> pumps it — and safe
    /// after it ends, where it is dropped rather than throwing into a background callback.
    /// </summary>
    public void Post(Action action)
    {
        if (_shutdown) return;
        try
        {
            _marshal.BeginInvoke(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { AgentLogger.LogException("UiDispatcher.Post", ex); }
            }));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The loop is gone. A queued menu rebuild has nothing left to rebuild.
        }
    }

    /// <summary>
    /// Run work on the owning thread and await its result. Used for the modal surfaces — message
    /// boxes and the folder dialog — where the caller is a Kestrel request or a background task
    /// that genuinely needs the user's answer.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        if (IsOwner)
        {
            try { return Task.FromResult(func()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_shutdown)
        {
            tcs.SetException(new InvalidOperationException("The SaveLocker UI thread has shut down."));
            return tcs.Task;
        }

        try
        {
            _marshal.BeginInvoke(new Action(() =>
            {
                try { tcs.TrySetResult(func()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }));
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        return tcs.Task;
    }

    public void Dispose()
    {
        _shutdown = true;
        _marshal.Dispose();
    }
}
