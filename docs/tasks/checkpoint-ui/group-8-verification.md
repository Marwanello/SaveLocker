# Group 8 (console kit, top bar, Games page) — live verification through testenv

Run from the repo root in PowerShell. Every step names what you should SEE; a step that does not match is a failure to
report, not to work around. Rig: the Docker console + the Windows tray agent + the headless WSL agent. No Deck needed.

Two rig facts that bite here:
- `testenv down` ignores `-Only` and stops **everything** (console, tray, daemon). To take one agent down, run `down`,
  then `up -Only console` and `up -Only windows` (or `linux`).
- A second `testenv conflict` against the same console DB is not deterministic (see `testenv.ps1`'s header). For a
  second conflict after resolving the first, edit the save files instead (section 5).

## 0. Preconditions

1. `.\tests\testenv.ps1 clean` — expect no error.
2. `.\tests\testenv.ps1 build` — expect "build succeeded" for Windows and Linux and the image named `savelocker:test`
   (the red `NativeCommandError` lines are Docker writing progress to stderr under Windows PowerShell — not failures).
3. `.\tests\testenv.ps1 conflict -Windows -Wsl` — expect `seeded 'Conflict Game' on Windows + wsl.` and the WSL line
   `CONFLICT: your save diverged from the server`.
4. `.\tests\testenv.ps1 up`, then `.\tests\testenv.ps1 status` — console `v0.5.13-test @ <this commit>`, Windows
   `connected=True`, a daemon pid.
5. Open `http://localhost:5080` at a desktop width (1280×800 or wider).

## 1. Top bar (8a)

1. The bar is one row: mark + **SaveLocker** + a version pill; pill tabs **Games · Configuration · Audit log · Help ·
   What’s new** — the current one in the soft accent with an accent border, the rest plain until hovered.
2. On the right: **1 conflict · Nm** (soft accent), **Sync all** (filled, with a refresh icon), a **bell** with a red
   badge, and — only if an admin password is set — a lock icon. There is no ↻ refresh button anywhere.
3. Press Tab from the address bar: every tab and tool shows a 2 px accent focus ring.
4. Click the bell: a panel titled **Notifications** with an "N open" chip; the conflict row has a red dot, a title
   ("Conflict Game — sync paused"), the agent's message, a line `sync.conflict · LinuxTest · Nm ago`, and **Resolve**
   — no × (a conflict cannot be dismissed). Footer: "Agents report these…" and **Open audit log** (goes to Audit log).
   Escape closes it and returns focus to the bell.

## 2. Sync all, the rail and Cancel (8a)

1. With everything up, press **Sync all**. Expect: a 3 px accent rail across the full width under the bar with a moving
   sheen, and in place of the button a chip **Syncing · 0 of 2 machines done** plus **Cancel**.
2. Within ~20 s the chip steps to **1 of 2**, then the batch ends: the chip and rail disappear and a toast says
   **Synced 2 machines.** The page around them must not flicker or replay its entrance animation during the ticks.
3. Cancel: `.\tests\testenv.ps1 down`, `.\tests\testenv.ps1 up -Only console`, `.\tests\testenv.ps1 up -Only windows`
   (so WSL stays down but was seen < 3 min ago). Reload and press **Sync all** within those 3 minutes. Press **Cancel**
   while the chip still reads 0 of 2 — toast **Withdrew 1 machine.**; once Windows finishes, **Synced 1 machine.
   1 cancelled before starting.**
4. Audit log: a `command.cancel` row for LinuxTest. (Or `GET /api/commands`: that command's `status` is `Cancelled`,
   `result` "Cancelled from the console before any agent picked it up.")
5. `.\tests\testenv.ps1 up -Only linux` to bring WSL back.

## 3. The Games page (8c)

1. **List** (default): a 250 px sidebar — header `GAMES · 1` and a List/Grid switch with icons, a row per game (cover or
   initials tile, name, `Nm ago · size`, a state dot — red while in conflict), **+ Add game** in the footer. The current
   row is soft-accent.
2. The page: cover (hover → pen → art picker), the name as a 27 px title, `N versions · size · latest Nm ago`, chips
   (**Conflict** · **Manual** · **Keep: server default**), a machine picker with **Push** / **Pull**, **Refresh art**.
3. Below: a red **banner** "WinTest and LinuxTest both wrote since <id>" with **Why did this happen?** and **Resolve**;
   four stat tiles (Latest version, Stored, Machines, Lease); **Versions** (left) beside **Save folders**, **Rules**,
   **Exclude patterns** (right); **Remote commands for this game** full width (or its empty state).
4. **Grid**: switch to Grid → the sidebar goes, a full-width wall of tiles (3:4 cover, red **Conflict** chip, name,
   dot · time · size) under a "Games · N tracked" head. Click a tile → the game page with **← All games**; back →
   the wall; switch to List. Reload: the last choice is remembered.

## 4. Resolve — keep one (8c)

1. Click the top bar's **1 conflict** pill (or the bell's **Resolve**): the page opens with the resolve panel already
   expanded — "Resolve — which save is your real progress?", two side cards (machine · id, size · time, *N file ·
   newest change …*), **Keep the WinTest save**, **Keep the LinuxTest save**, **Keep both — X's as Latest**, and the
   "Playing solo? Use Newest wins" hint. Neither side is highlighted.
2. Click **Keep the WinTest save**: it expands in place into a sentence ("WinTest's save … becomes Latest and both
   machines in this conflict pull it. …") with one button naming the effect, and focus is on that button. Confirm.
3. Toast **Kept the WinTest save. Both machines will pull it.**; the banner and the top-bar pill disappear.

## 5. Resolve — keep both, with the page left open (8c, and the bug it fixed)

1. Leave the game page open. Make a new conflict by editing each side's save so they diverge:
   - WSL: `wsl -d Ubuntu -- bash -c "echo change >> /home/maro/savelocker-test/conflict-save/save.txt"` — wait ~20 s
     (it fast-forwards: the WSL save becomes Latest).
   - Windows: `Add-Content "$env:LOCALAPPDATA\Temp\savelocker-conflict-test\win\save.txt" "change"` — within ~30 s a
     new conflict appears (the pill comes back on the next 15 s poll).
2. Edit the Windows file once more and wait ~45 s **without reloading** (the server folds the newer save into the same
   conflict).
3. Open **Resolve**: both side cards show a machine and size (neither shows only an id), and **Keep both** names the
   machine whose save is newer by its time. Confirm **Keep both saves**.
4. Toast "Kept the WinTest save and the other as a backup…"; under Versions both snapshots carry **Protected**.

## 6. Versions (8c)

1. **Unprotect** on a protected row → a sentence ("Retention may then delete it…") + **Unprotect <id>** → the chip goes.
2. **Backups** tab → **Set as Latest** on a backup → "Every machine that syncs Conflict Game is told to pull …" →
   **Make <id> Latest** → toast; the head changes.
3. **Delete** on another backup → "Its archive is permanently removed…" → **Delete <id>** → toast, row gone.
4. Rules → Keep: type `1`, **Save** → the Keep chip reads **Keep 1**, and Versions' button reads **Prune N versions**
   with the real N. Confirm it → toast **Removed N versions.** (the same N); the button now reads **Nothing to prune**.
   With Keep blank the button reads **Apply retention** (the server default is not known to the console).

## 7. Save folders, Push/Pull, exclude patterns (8c)

1. Save folders: the template (green if it is a `<token>` template), then one block per machine with its folder,
   "last upload Nm ago", **Push**, **Pull**, **Edit**, **Force…**. **Force…** reveals **Force pull** / **Force push**,
   each expanding into a sentence naming what it overwrites. Don't confirm those unless you mean it.
2. **Push** on LinuxTest: toast "Queued a push on LinuxTest…"; the sidebar row shows a red **Queued** chip until the
   next poll, and Remote commands shows the row go Queued → **Done** with the agent's own result text.
3. Exclude patterns: **Preview what is skipped** → "Nothing in the latest save matches."; type `**` + Enter → a chip, and
   "Saving would leave 1 file in the latest save out of future uploads."; add `saves/../x` → a red refusal from the
   server and **Save patterns** disabled; **Discard changes** → back to the saved list. Inherited defaults show as
   dashed chips. **Glob syntax** opens Help.

## 8. The bell's other actions (8a)

1. Hide the WSL save folder: `wsl -d Ubuntu -- mv /home/maro/savelocker-test/conflict-save /home/maro/savelocker-test/conflict-save.hidden`.
   Within ~1 min the bell's badge turns **amber** (a Warning); the game's dot and state chip read **Needs attention**
   (and in Grid, a **Retrying** chip).
2. Bell → **Set folder** on "Conflict Game save folder is missing": the game opens with LinuxTest's folder field in
   edit mode, focused, pre-filled, labelled "Save folder on LinuxTest". Escape.
3. Restore: `wsl -d Ubuntu -- mv /home/maro/savelocker-test/conflict-save.hidden /home/maro/savelocker-test/conflict-save`.
   Dismiss the row with its × → the panel shows **0 open · Nothing to report · A healthy fleet is quiet…** and the badge
   is gone.
4. A `push.failed` row, when one exists, has **Retry** (queues an ordinary push for that machine and game).

## 9. Themes and contrast

1. Configuration → Appearance → Light, then Dark: every surface above stays legible; the current tab / row / banner keep
   the soft accent; nothing turns dark-on-dark.
2. Optional, precise: the contrast walk in [[Gotchas]] → *Web console* — only uppercase eyebrows on `--faint` and
   disabled buttons may measure under 4.5:1.

## 10. Suites

- `.\tests\run-console-security-tests.ps1` → **189 passed** (API-03 is the cancel route).
- `.\tests\run-appearance-consistency-tests.ps1` → **35 passed** (the last new check: no `alert(`/`confirm(`/`prompt(`
  under `web/src/components/game`).
- `cd web; npm run build; npm run lint` → clean.

When done: `.\tests\testenv.ps1 down` (or `clean`).
