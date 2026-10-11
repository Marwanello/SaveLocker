# Notifications: buttons that act, and a check on real installs (left by Group 7)

**Built 2026-10-11 (branch `notification-follow-ups`).** Steps to verify: [[verification]].

- (a) *"Retry now" / "Install now" on a Windows toast* — **done, verified live.** The supported route
  turned out to be the Community Toolkit's: a per-user COM activator (`HKCU` CLSID with
  `LocalServer32`, `CustomActivator` under the identity key) rather than the shortcut's
  `ToastActivatorCLSID` — a rig needs **no shortcut** for activation, and got one anyway for the
  header name. Toast actions with no `activationType` carry `retry:<gameId>` / `install:<version>`;
  the click lands in the running tray, or DCOM starts `SaveLocker.Agent.exe -ToastActivated` when it
  is not running, which leaves the click for the tray and starts it (`toast-action.json`). Measured
  on Windows 11 25H2: real click on **Retry now** retried a 413-rejected push; **Install now**
  downloaded, SHA-verified and ran the offered package with `/SILENT /FORCECLOSEAPPLICATIONS
  /NORESTART`, then exited as designed. Two bugs were found only by running it: a rig's
  `LocalServer32` initially omitted `--config` (the tray compared against its own env-influenced
  default), so a click with the tray closed loaded the **installed** config and started the
  installed agent — fixed, and re-verified end-to-end; and the host logged to the installed
  agent's log for the same reason. `testenv` now writes/removes the rig's shortcut and CLSID keys.
- (b) *Run the installer once* — still the one manual check; exact steps in [[verification]] §5.
  (The installer now also stamps `ToastActivatorCLSID` on the shortcut and writes the registry
  entries, so a fresh install's very first toast has working buttons.)
- (c) Done earlier (Deck Desktop Mode, 2026-09-27).
- (d) *The exe grew ~8 MB* — **measured: not shrinkable this way.** `CsWinRTIncludes` is a CsWinRT
  projection-*authoring* property; the Windows SDK projection is prebuilt and identical with or
  without it (173.1 MB publish, `Microsoft.Windows.SDK.NET.dll` 23.73 MB — unchanged).
  `Microsoft.Windows.UI.Xaml.dll` is not even in this toolchain's output. No code change; details in
  [[verification]] §6.
- (e) *`clean` printed "removed" over state that was still there* — **fixed.** It now asserts the
  state root is gone and throws naming the leftovers (the elevated-state case), instead of lying.
- (f) *The installed service never gets the desktop session's environment* — unchanged and still
  documented ([[Gotchas]] → Linux agent); nothing here adds a daemon-launched desktop child, and
  the Windows side is unaffected.
