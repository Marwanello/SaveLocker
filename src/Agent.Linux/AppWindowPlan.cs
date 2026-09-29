namespace SaveLocker.Agent.Linux;

/// <summary>How to put the agent UI in its own window: the program to start, and what it is.</summary>
/// <param name="Description">A sentence for <c>savelocker open</c> to print, or for a log.</param>
public sealed record AppWindowPlan(string FileName, IReadOnlyList<string> Args, string Description, bool IsAppWindow);

/// <summary>
/// The decision behind <c>savelocker open</c>, with no I/O so it can be tested: given what is installed,
/// which program opens the UI, and how.
/// <para>
/// A Chromium-family browser's <c>--app=</c> mode draws a window with no tabs and no address bar — which is
/// as close to "a small desktop app" as this project gets without shipping a second UI toolkit (WebKitGTK
/// was ruled out in Decisions → Linux UI: 665 MB+ as a Flatpak, and a system package cannot survive
/// SteamOS's immutable root). On a Deck that browser is usually a Flatpak, so those are looked for too.
/// Anything else falls back to the default browser, which shows the same page in a tab.
/// </para>
/// </summary>
public static class AppWindowPlanner
{
    /// <summary>Executables searched for on PATH, most likely first.</summary>
    public static readonly string[] Binaries =
    [
        "google-chrome-stable", "google-chrome", "chromium", "chromium-browser",
        "microsoft-edge-stable", "microsoft-edge", "brave-browser",
    ];

    /// <summary>Flatpak application ids, most likely first.</summary>
    public static readonly string[] FlatpakIds =
    [
        "com.google.Chrome", "org.chromium.Chromium", "com.microsoft.Edge", "com.brave.Browser",
    ];

    /// <summary>
    /// Adds <c>?app</c> before any <c>#route</c>: the flag agent-ui reads to know it is not inside a
    /// browser's own chrome and should draw its own header. Idempotent.
    /// </summary>
    public static string WithAppFlag(string url)
    {
        var hash = url.IndexOf('#');
        var head = hash < 0 ? url : url[..hash];
        var tail = hash < 0 ? "" : url[hash..];
        if (head.Contains("?app", StringComparison.Ordinal) || head.Contains("&app", StringComparison.Ordinal)) return url;
        return head + (head.Contains('?') ? "&app" : "?app") + tail;
    }

    public static AppWindowPlan Choose(
        string url, Func<string, bool> binaryOnPath, Func<string, bool> flatpakInstalled)
    {
        var appUrl = WithAppFlag(url);

        foreach (var bin in Binaries)
            if (binaryOnPath(bin))
                return new AppWindowPlan(bin, [$"--app={appUrl}"], $"in {bin}'s app window", IsAppWindow: true);

        foreach (var id in FlatpakIds)
            if (flatpakInstalled(id))
                return new AppWindowPlan("flatpak", ["run", id, $"--app={appUrl}"], $"in {id}'s app window", IsAppWindow: true);

        // The plain URL, not the app one: in an ordinary tab the browser already draws its own chrome.
        return new AppWindowPlan("xdg-open", [url], "in your default browser", IsAppWindow: false);
    }
}
