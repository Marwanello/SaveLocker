# Notification follow-ups — how to verify (testenv)

What this branch adds: toast buttons that **act** ("Retry now" on a failed push, "Install now" on an
available update), through a COM activator; the rig's own Start-menu toast identity; `clean` that
verifies its state removal; and a measured answer to the `CsWinRTIncludes` size question. The
verification below is what was run on 2026-10-11; every expected observation is a real one from that
pass.

Run from the repo root in Windows PowerShell. `.\tests\testenv.ps1` is the rig — the installed agent
is never touched **as long as the steps stay on :5188 and the rig's state root**.

## 0. Build and start

```powershell
.\tests\testenv.ps1 clean
.\tests\testenv.ps1 build -Only windows
.\tests\testenv.ps1 build -Only console
.\tests\testenv.ps1 up -Only console -ConsoleEnv "Storage__MaxUploadMb=1"
```

Then seed a game and start the tray (the 1 MB upload cap is what forces a failed push):

```powershell
$env:SAVELOCKER_STATE_ROOT = "$env:LOCALAPPDATA\SaveLocker-test"
$dir = "$env:TEMP\savelocker-retry-test\win"
New-Item -ItemType Directory -Force $dir | Out-Null
'small save v1' | Set-Content "$dir\save.txt"
$dll = "src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.dll"
dotnet $dll set-server --url http://localhost:5080
dotnet $dll register --name WinRetry
dotnet $dll add-game --name "Retry Game" --dir $dir
dotnet $dll push "Retry Game"
# starts the tray and prints "toast shortcut: ...\Start Menu\Programs\SaveLocker Test 5188.lnk" —
# the Start-menu shortcut that names the toast header. No toast yet: §1 produces the first one.
.\tests\testenv.ps1 up -Only windows
```

## 1. Retry now — a real click on a real toast

The rig only toasts when something **fails**, so **this step is what raises the first toast** — starting
the tray, and a successful push, are both silent by design.

1. Write a save above the cap and watch the log:
   ```powershell
   $bytes = New-Object byte[] 2MB; (New-Object Random).NextBytes($bytes)
   [IO.File]::WriteAllBytes("$env:TEMP\savelocker-retry-test\win\big.bin", $bytes)
   Get-Content "$env:LOCALAPPDATA\SaveLocker-test\SaveLocker\agent.log" -Tail 5
   ```
   Expect within ~20 s: `push rejected by the server (413)` then
   `notification shown: 'Retry Game: push failed'`.
2. Look at the toast (bottom-right of the primary monitor). Expect header **SaveLocker Test 5188**
   with the brand mark and the padlock logo, and three buttons: **Retry now / Open game / Dismiss**
   (Error severity keeps it on screen — no time pressure).
3. Lift the cap so the retry can succeed, then click **Retry now** with the mouse:
   ```powershell
   .\tests\testenv.ps1 up -Only console          # restarts the console WITHOUT -ConsoleEnv
   ```
   Expect in the log: `[Retry Game] pushed new version.` — the click really performed the push —
   and a **Retry Game: pushed the new save.** confirmation toast. The push-failed toast is gone
   from Action Center (the condition cleared).
4. If you want to re-run it: write to the save folder again (with the cap back on, step 0's `up`).

If no toast appears: step 1's log must show `push rejected by the server (413)` and
`notification shown: 'Retry Game: push failed'` — the watcher's settle gate runs first, so give it
~20 s. `connection refused … :5080` instead means the console is down (`docker ps` for
`savelocker-test`). No rejection line at all means the push never failed — the console was most likely
started without `-ConsoleEnv "Storage__MaxUploadMb=1"`, or the file is under the cap. If
`notification shown` *is* in the log but no banner popped, Windows routed it to the Action Center
(Focus Assist / notification settings) — it is still listed there.

## 2. Install now — on a rig, never a real installer

Use a **harmless stand-in** (never `installer\dist\`): build a no-op exe that records how it was run.

```powershell
$d = "$env:TEMP\sl-noop-installer"
dotnet new console -o $d --force
# replace Program.cs with a Main that writes %TEMP%\sl-noop-installer-ran.txt (path + args)
dotnet publish $d -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$d\out"
Remove-Item "$env:TEMP\sl-noop-installer-ran.txt" -ErrorAction SilentlyContinue
curl.exe -X POST "http://localhost:5080/api/admin/agent-installer?platform=win-x64&version=9.9.9" -F "file=@$d\out\sl-noop-installer.exe"
```

1. Force a fresh update check — the background check has a 24 h cooldown in `config.json`, so either
   clear `LastUpdateCheck` (below) or use the tray menu's *Check for Updates*:
   ```powershell
   $cfg = "$env:LOCALAPPDATA\SaveLocker-test\SaveLocker\config.json"
   $j = Get-Content $cfg -Raw | ConvertFrom-Json; $j.LastUpdateCheck = $null
   $j | ConvertTo-Json -Depth 20 | Set-Content $cfg
   # restart the tray in the same breath so the 5 s startup check runs
   .\tests\testenv.ps1 up -Only windows
   ```
2. Expect `Update check: v9.9.9 available` and a toast **SaveLocker 9.9.9 is available** with
   **Install now / Later**. (A version like `9.9.9-ci` is rejected by `UpdateChecker` —
   "unparseable version"; use a plain dotted version.)
3. Click **Install now**. Expect the **Downloading SaveLocker v9.9.9…** toast, then
   `%TEMP%\sl-noop-installer-ran.txt` containing
   `args=/SILENT /FORCECLOSEAPPLICATIONS /NORESTART` — the exact command line the real installer
   gets — and the tray exits (the real installer replaces it; with the stand-in, restart it yourself).
   *If a mouse click misses the short-lived Info banner* (an Info toast auto-dismisses after ~5 s and
   moves to Action Center; Error reminders like Retry now stay put), drive the same call the shell
   makes: `CoCreateInstance` the registered CLSID (`{02C91444-…4D3AB0}` for :5188) and call
   `INotificationActivationCallback.Activate("SaveLocker.Test.5188", "install:9.9.9", …)` from a small
   `Add-Type` C# helper — the interface is vtable-based, so plain PowerShell cannot invoke it directly.
   That is how this step was verified, after the real mouse click on the retry toast above proved the
   shell's own button routing.

## 3. The tray is not running — the click still works

1. Make a push fail again (step 0's capped console + a new write), then lift the cap and **stop the
   tray** (kill the tray process; the standing toast stays in Action Center).
2. Click **Retry now** (or fire the activation as in step 2.3). Expect the tray to come back by
   itself and the retry to succeed:
   `toast activation: 'retry:<id>' while no tray was running — leaving it for the tray and starting it.`
   then `pushed new version.`
   This path is DCOM starting `SaveLocker.Agent.exe -ToastActivated` (the registered
   `LocalServer32`), which writes the click to `toast-action.json` and starts the tray with the
   rig's own environment.

## 4. What `clean` must remove (and prove it)

```powershell
.\tests\testenv.ps1 clean
```

Expect all of these **absent** afterwards:

| Item | Where |
|------|-------|
| State + builds | `%LOCALAPPDATA%\SaveLocker-test` |
| Rig toast shortcut | `…\Start Menu\Programs\SaveLocker Test 5188.lnk` |
| Toast identity | `HKCU\Software\Classes\AppUserModelId\SaveLocker.Test.5188` |
| Activator CLSID | `HKCU\Software\Classes\CLSID\{02C91444-…4D3AB0}` (the port lives in the CLSID) |
| Toast history record / logo | `…\Notifications\Settings\SaveLocker.Test.5188`, `%TEMP%\savelocker-toast-logo-5188*.png` |

`clean` now also **refuses to lie**: if `Remove-Item` returns without throwing but the state dir is
still there (the elevated-shell case seen twice), it throws naming the leftovers instead of printing
"removed". The normal path prints `removed …` and the checks above pass.

## 5. Real installs — the two checks that are NOT testenv's to make

The rig proves the mechanism (its own shortcut, its own CLSID, its own state); these two are about
the *installed* agent:

1. **Run the installer once** (`installer\build-installer.ps1`, then the `-Setup-…exe`) and look at a
   real toast:
   - Header reads **SaveLocker** with the app icon (from the installer's shortcut carrying the AUMID
     and now `ToastActivatorCLSID`).
   - A "Retry now"/"Install now" button *works* — the installer's registry entries are the same ones
     the tray rewrites at every start, so a machine that has never run the new tray is still covered.
   - With the tray closed, clicking still starts it (the `LocalServer32` write, no rig involved).
2. **Deck/Linux:** nothing here changes the Linux notifier (one button, no callbacks), so the Group 7
   Deck checks stand as they were. The installed `savelocker.service` environment caveat
   (`Gotchas` → Linux agent) is unchanged: on Windows it does not apply, and no new daemon-launched
   desktop child was added.

## 6. The size question (`CsWinRTIncludes`) — measured, not changed

`<CsWinRTIncludes>Windows.UI.Notifications;Windows.Data.Xml.Dom</CsWinRTIncludes>` changes
**nothing**: a self-contained publish is byte-for-byte the same (173.1 MB / 477 files,
`Microsoft.Windows.SDK.NET.dll` 23.73 MB). That property belongs to CsWinRT *projection generation*
for a component you author; the Windows SDK's projection arrives prebuilt through
`Microsoft.Windows.SDK.NET.Ref` and no ref-level switch trims it. `Microsoft.Windows.UI.Xaml.dll` is
not in this toolchain's output at all (the older "+8 MB unused Xaml" note described a
`UseUwp`-profile reference this project does not take). The exe size is therefore unchanged by this
branch — the installer's LZMA2 pass already compresses the projection to ~6 MB.
