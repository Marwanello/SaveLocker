# Session summary — 2026-10-11/12 — Notification follow-ups: toast buttons that act (PR #66)

Standalone: the facts below do not need the rest of the vault.

**PR:** #66 on `Marwanello/SaveLocker`, *Notification follow-ups: acting toast buttons (COM activator), rig
toast identity, installer wiring, clean guard*. Head branch `notification-follow-ups` (worktree
`.claude/worktrees/notification-follow-ups`), base `main`. Commits `97041bc` (the feature), `4ddb4d2` (the
same-day `testenv` fix), plus a clean merge of `origin/main`. Open at the time of writing.

## What was asked

1. Work the Backlog item left by Group 7 (`tasks/notification-follow-ups/summary.md`): (a) toast buttons
   that **act**, (b) a real-install check, (d) the `CsWinRTIncludes` size question, (e) a `clean` that
   stops lying about state removal.
2. "Tell me how to verify using testenv."
3. Open a PR on the `Marwanello` fork.
4. Mark the task done, append the summary to `progress.md`, save a standalone summary here.

## What was done

**Buttons that act (a).** Toast actions with no `activationType` carry `retry:<gameId>` / `install:<version>`
and are delivered through a per-user **COM activator** (`src/Agent/ToastActivation.cs`): `HKCU` CLSID with
`LocalServer32 -ToastActivated`, plus `CustomActivator` under the identity key — the Community Toolkit's
shape, measured; the shortcut's `ToastActivatorCLSID` is *not* what makes a click travel. While the tray
runs the click lands in it; with the tray closed, DCOM starts the host (`ToastActivationHost.cs`), which
sets its state root from `--config` before any logging, leaves the click in `toast-action.json` and starts
the tray. A rig gets a deterministic per-port CLSID, its own `SaveLocker Test <port>` Start-menu shortcut
(written by `up`, removed by `clean`) and registry keys; `installer/SaveLocker.iss` stamps the same
activator entries (`uninsdelete*`) so a fresh install's first toast works.

**`clean` refuses to lie (e).** It verifies the state root is actually gone and throws naming the leftovers
instead of printing "removed" over them; `Get-TestTray` gained the apphost shape the toast-activation host
starts, and later (see below) any worktree's tray.

**(d) measured, not changed.** `CsWinRTIncludes` shrinks nothing: 173.1 MB publish,
`Microsoft.Windows.SDK.NET.dll` 23.73 MB, with and without it — the property is for projection *authoring*,
and `Microsoft.Windows.UI.Xaml.dll` is not in this toolchain's output at all.

**Two bugs only running found:** a rig's `LocalServer32` first omitted `--config`, so a tray-closed click
loaded the **installed** config and briefly started the installed agent (its one log line lives in
`%PROGRAMDATA%\SaveLocker\agent.log`; the `LastUpdateCheck` it refreshed was reset); and the host logged
into the installed agent's log for the same reason. Both fixed and re-verified.

**Docs (this session's ask).** `docs/tasks/_done/notification-follow-ups/{summary,verification}.md` (the
folder moved out of `Backlog.md`, which no longer lists it), a row in `logs/shipped-2026-10.md`, the
`CONTEXT.md` entry, this file. `AGENTS.md`'s handoff step now says `docs/tasks/_done/`, matching what the
Backlog states and what the last two sessions did.

## Verification (final tree, after merging `origin/main`)

- `dotnet test` **420/420** — all green; main's #63 fixed the one machine-specific
  `ArcadeSavesTests.Supermodel_nvram…` leak this branch had carried
- `run-appearance-consistency-tests` **50/50** (incl. the new installer/agent CLSID + AUMID drift checks
  and the notice-route↔`route.ts` contract)
- Installer compiles under ISCC; full agent build at its 1-warning baseline; `origin/main` merged with
  **no conflicts**
- **Live through `testenv`** (a real mouse click on a real toast): **Retry now** retried a 413-rejected
  push (`pushed new version.`); **Install now** downloaded, SHA-verified and ran a stand-in package with
  `/SILENT /FORCECLOSEAPPLICATIONS /NORESTART`, the tray exiting as designed; with the tray closed, DCOM's
  host started it and the retry succeeded

## The debugging session worth remembering

The first live walks showed **"no toast appears"** twice, then an endless **401 flood** surviving `clean`
and restarts. Three findings:

- The rig only toasts when something fails — `up -Only windows` alone never pops anything. The first toast
  is §1's >1 MB write hitting the capped console (413 → `notification shown: 'Retry Game: push failed'`).
  A brand-new AUMID gains its `HKCU\…\Notifications\Settings\SaveLocker.Test.<port>` record only once
  Windows has accepted a toast — a useful "did it ever render" probe.
- The 401s came from a tray started by **another worktree** (`fix-backlog-quick-wins`) that survived this
  worktree's `clean`/`up`: `Get-TestTray` was scoped to the calling worktree's own root, so `clean`
  deleted the shared state *under* the foreign tray, the next `up`'s tray could not take :5188, and the
  stale one kept serving with its old machine key. Fixed in `4ddb4d2`: any tree build matches by its
  `\src\Agent\bin\` path (the installed agent never lives there). Recorded in `Gotchas`.
- The verification doc gained a "no toast appears" troubleshooting block covering all of it.

## Not done / caveats

- **The real installer run is still the one manual check** — `docs/tasks/_done/notification-follow-ups/verification.md`
  §5 (with §0–§4 being the testenv walkthrough).
- Two sessions driving the shared rig at once still stomp each other's state — the tray-stopping fix
  removes the stranded-tray half, not the coordination half.
- Release notes for the next tag must cover: toast buttons that act (Retry now / Install now).
