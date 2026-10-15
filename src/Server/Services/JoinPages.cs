using System.Net;
using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>
/// The public pages and scripts behind a join link (tasks/enrollment-options, option B).
/// Self-contained on purpose: inline CSS, system fonts, no request to anywhere but this server — a
/// join page has to work on a LAN with no internet. Everything a request supplies (the Host behind
/// the server URL) is HTML-encoded before it is written into the page.
/// </summary>
public static class JoinPages
{
    public const string ShellContentType = "text/x-shellscript";
    public const string DesktopContentType = "application/x-desktop";


    /// <summary>
    /// The pairing page's script, served from <c>/join.js</c> rather than inlined: the dashboard's
    /// CSP is <c>script-src 'self'</c> and silently blocks an inline script, so inlined the page
    /// rendered but lost its Copy buttons and the Linux tab (found in review). A same-origin
    /// external script is exactly what that policy was built to allow.
    /// </summary>
    public const string Script = """
        (function () {
          function fallback(text, done) {
            var ta = document.createElement('textarea');
            ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
            document.body.appendChild(ta); ta.select();
            try { document.execCommand('copy'); done(); } catch (e) {}
            document.body.removeChild(ta);
          }
          function copy(text, el, done) {
            if (navigator.clipboard && navigator.clipboard.writeText) {
              navigator.clipboard.writeText(text).then(done, function () { fallback(text, done); });
            } else { fallback(text, done); }
          }
          document.querySelectorAll('[data-copy]').forEach(function (el) {
            el.addEventListener('click', function () {
              copy(el.getAttribute('data-copy'), el, function () {
                var label = el.querySelector('.lbl') || el;
                if (label.dataset.was === undefined) label.dataset.was = label.textContent;
                label.textContent = 'Copied';
                label.classList.add('copied');
                setTimeout(function () {
                  label.textContent = label.dataset.was; label.classList.remove('copied');
                }, 1600);
              });
            });
          });
          var tabs = document.querySelectorAll('#tabs button');
          function show(which) {
            tabs.forEach(function (t) { t.classList.toggle('on', t.dataset.tab === which); });
            document.querySelectorAll('[data-panel]').forEach(function (p) {
              p.classList.toggle('hidden', p.dataset.panel !== which);
            });
            document.querySelectorAll('[data-os]').forEach(function (p) {
              p.classList.toggle('hidden', p.dataset.os !== which);
            });
          }
          tabs.forEach(function (t) { t.addEventListener('click', function () { show(t.dataset.tab); }); });
          if (!/Windows/i.test(navigator.userAgent)) show('linux');
        })();
        """;

    /// <summary>
    /// The pairing page. <paramref name="state"/> decides which card is shown; only a valid code
    /// gets the install-and-enroll actions.
    /// </summary>
    public static string PairingPage(
        string code, EnrollmentService.JoinCodeState state, string serverUrl,
        DateTime? expiresAt, bool windowsInstallerHosted, bool linuxPackageHosted)
    {
        var joinUrl = $"{serverUrl}/join/{code}";
        var host = WebUtility.HtmlEncode(Uri.TryCreate(serverUrl, UriKind.Absolute, out var u) ? u.Authority : serverUrl);
        var body = state switch
        {
            EnrollmentService.JoinCodeState.Valid => ValidBody(code, joinUrl, serverUrl, expiresAt, windowsInstallerHosted, linuxPackageHosted),
            EnrollmentService.JoinCodeState.Used => StateBody(
                "This link has already been used",
                "A machine was enrolled with it. If you need to enroll another one, create a new link in the console — " +
                "each link works exactly once."),
            EnrollmentService.JoinCodeState.Expired => StateBody(
                "This link has expired",
                "Join links are short-lived on purpose. Create a new one from the console — it takes one click."),
            _ => StateBody(
                "This link is not valid",
                "It may have been revoked, or mistyped. Create a fresh link from the console: Configuration → Enroll a machine."),
        };

        return Template
            .Replace("@BODY@", body)
            .Replace("@HOST@", host)
            .Replace("@SERVER@", WebUtility.HtmlEncode(serverUrl));
    }

    private static string ValidBody(string code, string joinUrl, string serverUrl, DateTime? expiresAt,
        bool windowsInstallerHosted, bool linuxPackageHosted)
    {
        var shown = WebUtility.HtmlEncode(JoinCodes.FormatJoinCode(code));
        var link = WebUtility.HtmlEncode(joinUrl);
        var localUi = WebUtility.HtmlEncode($"http://127.0.0.1:5178/#enroll:{Uri.EscapeDataString(joinUrl)}");
        var minutes = expiresAt is { } at ? (int)Math.Max(1, Math.Ceiling((at - DateTime.UtcNow).TotalMinutes)) : 0;
        var expiry = expiresAt is null ? "" : $"Link valid for about {minutes} more minute{(minutes == 1 ? "" : "s")}";

        var windows = windowsInstallerHosted
            ? $"""
               <p class="lead">The installer enrolls this PC as it installs — the link fills in the server and the code, so there is nothing to browse for and nothing to type.</p>
               <div class="row">
                 <a class="btn primary" href="/join/{WebUtility.HtmlEncode(code)}/setup.exe" data-copy="{link}">Download &amp; enroll</a>
                 <span class="fine">SaveLocker-Agent-Setup.exe · ~68 MB</span>
               </div>
               <p class="fine">The join link is copied to your clipboard too — paste it into the installer's
                  <b>Enroll this machine</b> box when it opens (Ctrl+V).</p>
               """
            : """
              <p class="lead">No Windows installer is hosted on this server yet.</p>
              <p class="fine">Ask the server's admin to upload one under <b>Configuration → Agent updates</b>,
                 then reopen this link.</p>
              """;

        var linux = linuxPackageHosted
            ? $"""
               <p class="lead">Download the installer and double-click it — a terminal opens, installs the agent and enrolls this device.
                  On a Steam Deck, do this in Desktop Mode.</p>
               <div class="row">
                 <a class="btn primary" href="/join/{WebUtility.HtmlEncode(code)}/install.desktop" download="Install SaveLocker.desktop">Download installer</a>
                 <span class="fine">Install SaveLocker.desktop</span>
               </div>
               <p class="fine">Your file manager asks once whether to trust a downloaded launcher — choose
                  <b>Execute</b> (KDE) or <b>Allow Launching</b> (GNOME). Prefer a terminal, or on SSH? The same thing in one line:</p>
               <div class="term"><span class="cmd">$ curl -fsSL {link}/install.sh | sh</span></div>
               <div class="row">
                 <button type="button" class="btn" data-copy="curl -fsSL {link}/install.sh | sh">Copy the command</button>
                 <span class="fine">Also shown on the command line: <span class="mono">{shown}</span></span>
               </div>
               """
            : """
              <p class="lead">No Linux package is hosted on this server yet.</p>
              <p class="fine">Ask the server's admin to upload one under <b>Configuration → Agent updates</b>,
                 then reopen this link.</p>
              """;

        return $"""
            <div class="brand">{Mark}<span>Save<span class="accent">Locker</span></span></div>
            <h1>Pair this device</h1>
            <p class="sub">with <b>@HOST@</b> · <span class="mono">@SERVER@</span></p>
            {(expiry == "" ? "" : $"<span class=\"chip-warn\">{WebUtility.HtmlEncode(expiry)}</span>")}

            <div class="seg" id="tabs" role="group" aria-label="Device type">
              <button type="button" data-tab="win" class="on">Windows</button>
              <button type="button" data-tab="linux">Linux &amp; Steam Deck</button>
            </div>

            <section class="panel" data-panel="win">
              {windows}
            </section>

            <section class="panel hidden" data-panel="linux">
              {linux}
            </section>

            <div class="divider"><span>Already have an agent on this device?</span></div>
            <div data-os="win">
              <p><a class="btn" href="{localUi}">Open the agent and finish</a></p>
              <p class="fine">Opens the agent UI on <i>this</i> device with the link filled in — one click to enroll.
                 (That only works when the agent is already installed and running here.)</p>
            </div>
            <div data-os="linux" class="hidden">
              <p><a class="btn" href="{WebUtility.HtmlEncode(JoinLinks.AgentLink(joinUrl))}">Open the agent and finish</a></p>
              <p class="fine">Opens the SaveLocker window on <i>this</i> device with the link filled in — one click to enroll.
                 It starts the agent if it is not running. Nothing happens? That agent predates the link;
                 <a href="{localUi}">open it here instead</a> while it is running.</p>
            </div>

            <p class="trust">Only continue if you recognise this server. Nothing is signed — the console that
               showed you this link is the trust anchor, and over plain http the traffic is unencrypted.
               Any device this link enrolls can read and write every game's saves.</p>
            """;
    }

    private static string StateBody(string title, string text) => $"""
        <div class="brand">{Mark}<span>Save<span class="accent">Locker</span></span></div>
        <h1>{WebUtility.HtmlEncode(title)}</h1>
        <p class="lead">{WebUtility.HtmlEncode(text)}</p>
        <p><a class="btn" href="/">Open the SaveLocker console</a></p>
        """;

    /// <summary>
    /// The one-line installer behind <c>/join/{code}/install.sh</c> (and what the pairing page's
    /// command runs). It downloads the hosted Linux tarball, runs its install.sh, then enrolls with
    /// <c>savelocker enroll --url</c> — the same command a person would type, scripted.
    /// </summary>
    public static string InstallScript(string serverUrl, string code)
    {
        // Placeholders, not interpolation: the script is full of ${...} and doubling every brace
        // would be the least readable part of this feature.
        return ScriptTemplate
            .Replace("@SERVER@", serverUrl)
            .Replace("@CODE@", code);
    }

    /// <summary>
    /// The launcher behind <c>/join/{code}/install.desktop</c> (see <see cref="JoinLinks.InstallLauncher"/>).
    /// Null when the server's public URL is not plain enough to paste into a shell command.
    /// </summary>
    public static string? InstallLauncher(string serverUrl, string code) =>
        JoinLinks.InstallLauncher($"{serverUrl}/join/{code}",
            Uri.TryCreate(serverUrl, UriKind.Absolute, out var u) ? u.Authority : serverUrl);

    private const string Mark =
        """<svg width="26" height="26" viewBox="0 0 32 32" aria-hidden="true" style="flex-shrink:0"><rect x="12" y="3" width="8" height="4" fill="var(--accent)"/><rect x="8" y="7" width="4" height="5" fill="var(--accent)"/><rect x="20" y="7" width="4" height="5" fill="var(--accent)"/><rect x="5" y="12" width="22" height="16" fill="var(--accent)"/><rect x="14" y="16" width="4" height="4" fill="var(--on-accent)"/><rect x="15" y="20" width="2" height="5" fill="var(--on-accent)"/></svg>""";

    private const string Template = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Pair this device — SaveLocker</title>
        <style>
        *{box-sizing:border-box;margin:0;padding:0}
        :root{
          --ink:#0f0f10;--panel:#141416;--raise:#191a1c;--tile:#1b1c1f;--fg:#f0eee9;--dim:#a09d97;
          --faint:#817e78;--line:#242427;--safe:#7fa96a;--watch:#d9a63f;--accent:#e0533c;--on-accent:#160f0e;
          --accent-soft:color-mix(in oklab,var(--accent) 14%,var(--panel));
          --accent-line:color-mix(in oklab,var(--accent) 42%,var(--panel));
          --accent-ink:color-mix(in oklab,var(--accent) 80%,var(--fg));
          --watch-soft:color-mix(in oklab,var(--watch) 14%,var(--panel));
          --watch-line:color-mix(in oklab,var(--watch) 40%,var(--panel));
          --watch-ink:color-mix(in oklab,var(--watch) 82%,var(--fg));
          color-scheme:dark;
        }
        @media (prefers-color-scheme: light){
          :root{
            --ink:#f5f3ef;--panel:#fffefc;--raise:#f0ede8;--tile:#ebe7e0;--fg:#191719;--dim:#57534d;
            --faint:#79756f;--line:#e2ddd5;--safe:#4c7b3e;--watch:#8a6212;--accent:#c0432c;--on-accent:#fff8f6;
            color-scheme:light;
          }
        }
        body{background:var(--ink);color:var(--fg);font:14px/1.5 system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;
             display:grid;place-items:center;min-height:100vh;padding:24px 16px;-webkit-font-smoothing:antialiased}
        .card{width:100%;max-width:560px;background:var(--panel);border:1px solid var(--line);border-radius:16px;
              padding:26px 24px;display:flex;flex-direction:column;gap:13px;
              box-shadow:0 18px 48px rgba(0,0,0,.28)}
        .brand{display:inline-flex;align-items:center;gap:9px;font-size:17px;font-weight:700;letter-spacing:-.03em}
        .brand .accent{color:var(--accent)}
        h1{font-size:24px;font-weight:700;letter-spacing:-.03em;line-height:1.1}
        .sub{color:var(--dim);font-size:13px}
        .lead{color:var(--dim);font-size:13px}
        .fine{color:var(--faint);font-size:11.5px;line-height:1.55}
        .mono{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:12px}
        .chip-warn{display:inline-flex;align-self:flex-start;align-items:center;gap:6px;font-size:11px;padding:4px 11px;
                   border-radius:999px;border:1px solid var(--watch-line);background:var(--watch-soft);color:var(--watch-ink)}
        .seg{display:inline-flex;align-self:flex-start;background:var(--raise);border:1px solid var(--line);border-radius:999px;padding:3px;gap:2px}
        .seg button{border:0;background:none;color:var(--dim);font:inherit;font-size:12.5px;font-weight:600;
                    padding:7px 14px;border-radius:999px;cursor:pointer}
        .seg button.on{background:var(--panel);color:var(--fg);box-shadow:0 1px 2px rgba(0,0,0,.22)}
        .panel{display:flex;flex-direction:column;gap:11px;border:1px solid var(--line);border-radius:12px;
               padding:15px;background:var(--raise)}
        .hidden{display:none !important}
        .row{display:flex;align-items:center;gap:10px;flex-wrap:wrap}
        .btn{display:inline-flex;align-items:center;justify-content:center;gap:7px;font:inherit;font-size:13px;font-weight:500;
             padding:9px 16px;border-radius:999px;border:1px solid var(--line);background:var(--panel);color:var(--fg);
             text-decoration:none;cursor:pointer;white-space:nowrap}
        .btn:hover{background:var(--tile)}
        .btn.primary{background:var(--accent);border-color:var(--accent);color:var(--on-accent);font-weight:600}
        .btn.primary:hover{filter:brightness(1.08)}
        .term{background:#0b0b0c;border:1px solid var(--line);border-radius:10px;padding:12px 14px;overflow-x:auto}
        .term .cmd{color:#f0eee9;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:12px;white-space:nowrap}
        .divider{display:flex;align-items:center;gap:10px;color:var(--faint);font-size:10.5px;letter-spacing:.1em;
                 text-transform:uppercase;margin-top:4px}
        .divider:before,.divider:after{content:"";height:1px;background:var(--line);flex:1}
        .trust{border-top:1px solid var(--line);padding-top:11px;color:var(--faint);font-size:11.5px;line-height:1.6}
        .copied{color:var(--safe)}
        .fine a{color:var(--accent-ink)}
        </style>
        </head>
        <body>
        <div class="card">
        @BODY@
        </div>
        <script src="/join.js"></script>
        </body>
        </html>
        """;

    private const string ScriptTemplate = """
        #!/bin/sh
        # SaveLocker — install this device and enroll it in one line.
        # Generated by the server for one join code; it expires and works once.
        set -e

        SERVER='@SERVER@'
        CODE='@CODE@'

        say() { printf '%s\n' "$*"; }

        if ! command -v curl >/dev/null 2>&1; then
          say "curl is required to install SaveLocker."
          exit 1
        fi

        TMP="$(mktemp -d)"
        trap 'rm -rf "$TMP"' EXIT INT TERM

        say "==> Downloading the SaveLocker agent from $SERVER ..."
        curl -fsSL "$SERVER/api/agent/installer/download?platform=linux-x64" -o "$TMP/savelocker.tar.gz"

        say "==> Unpacking..."
        mkdir -p "$TMP/out"
        tar -xzf "$TMP/savelocker.tar.gz" -C "$TMP/out"
        if [ ! -x "$TMP/out/SaveLocker/install.sh" ]; then
          say "!! The downloaded package does not look like a SaveLocker release."
          exit 1
        fi

        say "==> Installing (nothing needs root; everything stays in \$HOME)..."
        ( cd "$TMP/out/SaveLocker" && ./install.sh )

        AGENT="$HOME/.local/share/SaveLocker/savelocker"
        say "==> Enrolling this device..."
        "$AGENT" enroll --url "$SERVER/join/$CODE"

        say ""
        say "==> Checking the whole chain:"
        "$AGENT" doctor || true
        say ""
        say "Done. The agent keeps itself updated from here on."
        """;
}
