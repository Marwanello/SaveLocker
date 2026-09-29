# Group 9 (Backups, Configuration, Audit log, Help, What's new, sign-in) — live verification through testenv

Run from the repo root in PowerShell on the Windows box. Each step says what you should SEE. If a step doesn't match,
report it as a failure; don't work around it. The rig is the Docker console, the Windows tray agent and the headless
WSL agent. No Deck is needed.

Rig facts that matter here:
- `testenv down` ignores `-Only` and stops **everything**. To restart only the console, run `down` and then
  `up -Only console` (and `up -Only windows` / `up -Only linux` for the agents).
- The console's `/data` is the Docker volume `savelocker-test-data`. It survives `down`/`up` and an image rebuild;
  only `clean` deletes it. That is what makes the before-upgrade check (step 3) possible.
- `build` does not sync the WSL clone. Run `.\tests\testenv.ps1 sync` first if the WSL agent should run this branch
  (only the `StagedVersion` heartbeat field needs that; step 5.6 covers it).

## 0. Preconditions

1. `git switch group-9-ui-redesign`, then `.\tests\testenv.ps1 clean`. Expect no error.
2. `.\tests\testenv.ps1 sync`, then `.\tests\testenv.ps1 build`. Expect build success for Windows and Linux, and the
   image `savelocker:test` stamped `v<next>-test` (for example `0.5.13-test`).
3. `.\tests\testenv.ps1 conflict -Windows -Wsl`. Expect `seeded 'Conflict Game' on Windows + wsl.`. This gives the
   console a real game, versions and two machines.
4. `.\tests\testenv.ps1 up`, then `status`. Expect the console `v0.5.13-test @ <commit>`, Windows `connected=True`,
   and a daemon pid.
5. Open `http://localhost:5080` at 1280×800 or wider. Resolve the conflict (keep either side) so the pill is gone.

## 1. Backups — the page (9a)

1. The tab strip reads **Games · Configuration · Audit log · Backups · Help · What's new**. Click **Backups**.
2. You see the title **Backups** with "nightly snapshots of the database · keeps the newest 7". On the right are a chip
   and **Back up now**. The chip is green **Last just now** because `up` took a catch-up snapshot on the fresh
   volume; with no snapshot at all it would read amber **No snapshots yet**.
3. Four tiles:
   - **Snapshots** with the oldest date
   - **On disk** with `/data/backups`
   - **Archives** with "N versions · not included in snapshots"
   - **Next run** reading `03:00` and "in Xh Ym"
4. Under **Recent snapshots** there's a line saying what a snapshot holds (credential hashes, as sensitive as the
   admin password), then the table. The first row is a **Nightly** chip.
5. Press **Back up now**. The toast reads **Snapshot written. N KB.**, and a new top row appears named
   `savelocker-YYYYMMDD-HHMMSS-manual.db` with an amber **Manual** chip.
6. Press **Download** on the Manual row. A `.db` file saves. Open it with
   `python -c "import sqlite3,sys;print(sqlite3.connect(sys.argv[1]).execute('PRAGMA integrity_check').fetchone())" <file>`.
   It should print `('ok',)`.
7. Go to **Audit log**. There are `backup.manual` and `backup.download` rows, each naming the file.
8. Check the security headers from PowerShell:
   `curl.exe -s -o NUL -w "%{http_code}" http://localhost:5080/api/admin/backups/<that file>`
   - With no admin password set, it answers `200`, because the console is open.
   - Set a password in step 4.8 and repeat. Expect `401` without a session.
   - `curl.exe -s -D - -o NUL -H "X-Admin-Password: <pw>" http://localhost:5080/api/admin/backups/<file>` shows
     `Cache-Control: no-store`.
   - `.../api/admin/backups/..%2Fsavelocker.db` returns `404`.
9. Press **restoring one** in the note. The **Database backups (snapshots)** help article opens.

## 2. Backups — the schedule (9a + 9b)

1. Open **Configuration → Defaults & maintenance**. **Nightly database backup** is on and reads "VACUUM INTO a snapshot
   at 03:00, keep 7".
2. Switch it **off**. The toast reads "Nightly backups are off…". Go to **Backups**: **Next run** reads **Off ·
   Scheduled backups are off**.
3. Switch it back **on**, and change the hour to `04:00` and keep to `3`. The toast names the new schedule. **Backups**
   now reads **Next run 04:00** and "keeps the newest 3".
4. Press **Back up now** twice. The table never holds more than 3 rows (retention prunes).
5. The Audit log has `settings.backup` rows with "(was …)".

## 3. Before-upgrade snapshot (9a)

1. Note the current snapshot names on **Backups**.
2. Rebuild the console at a different version, keeping its volume:
   `.\tests\testenv.ps1 build -Only console -Version 0.5.99-test`, then `.\tests\testenv.ps1 down`,
   `.\tests\testenv.ps1 up -Only console`. Then run `up -Only windows` and `up -Only linux` to bring the agents back.
3. `status` shows the console `v0.5.99-test`. **Backups** has a new row `…-before-upgrade.db` with an amber **Before
   upgrade** chip.
4. Prove it predates the new start: download it and run
   `python -c "import sqlite3,sys;print(sqlite3.connect(sys.argv[1]).execute(\"select Value from Settings where Key='Server:LastStartedVersion'\").fetchone())" <file>`.
   It should print the **old** version (`0.5.13-test`), not `0.5.99-test`.
5. Run `down` and `up -Only console` again at the same version. **No** second before-upgrade row appears.
6. Rebuild at the normal version (`.\tests\testenv.ps1 build -Only console`, `down`, `up`) before continuing. That
   also takes a before-upgrade snapshot, which is expected.

## 4. Configuration (9b)

1. The head reads **Configuration** with "server settings · appearance · enrollment · storage" and a chip: amber
   **Open — no admin password** now, green **Connected as admin** once a password is set.
2. Row one is **Server** beside **Appearance**. Server shows:
   - Public URL, Storage (`/data/archives`), and Build `v0.5.13-test · commit … · built …` with a **Dev build** chip,
     **Copy**, and **Release notes**
   - Escalate: "Conflicts go overdue after 6 hours"
   - a green **meter**, with "N KB of saves across 1 game" and "X GB free of Y GB" under it. Compare Y and X with
     `docker exec savelocker-test df -h /data`; they should match within rounding.
   - the SteamGridDB block. **Clear** (only with a key) expands into a sentence and a **Clear the key** button.
3. Row two is **Enroll a machine** beside **Defaults & maintenance**.
   - Create an enrollment file: a `.json` downloads, the toast says it cannot be shown again, and the table shows a
     row with an amber **Valid · Nm left** chip and **Revoke**.
   - Press **Revoke**. It expands into a sentence and **Revoke the file**. Confirm; the chip turns grey **Expired**.
   - A file an agent used reads green **Used · <machine>**.
4. **Machines** is a table with an OS badge + name, Platform ("Windows 11 (build …)", "Ubuntu … · WSL"), Agent, a
   **Last seen** chip (green Online / amber Offline · Nm ago / grey Never reported), Games, and **Delete** (inline
   confirmation, not a browser dialog).
   - While a game is being played or a push holds a lease, the machine also shows **Force-release 1 lease**. It
     expands and warns before releasing.
5. **Agent updates** sits under Machines. **Edit** now opens the editor **inside the card**, not a pop-up. Press
   **Delete** on a hosted package (when there is one): it expands in place.
6. Row four holds **Admin password**. Set a password there. A toast confirms and you stay signed in.
7. Anywhere on Configuration, there must be **no browser `alert`/`confirm` dialog** at any point.
8. Leave the password set; step 6 needs it.

## 5. Editable default excludes — proved on the WSL agent (9b)

1. In **Defaults & maintenance → Default exclude patterns**, the chips are the config defaults (`*.tmp *.log *.bak
   Thumbs.db desktop.ini`), with "From the server configuration until saved here."
2. Type `a/../b` + Enter, then **Save defaults**. A red refusal from the server appears under the chips and nothing is
   saved. Remove the `a/../b` chip.
3. Add `*.dmp` + Enter, then **Save defaults**. The toast reads "Saved the default exclude patterns…" and the line under
   the chips now reads "Saved from this console."
4. Go to **Games → Conflict Game → Exclude patterns**. `*.dmp` appears among the dashed **Inherited from the server**
   chips (with an "edit in Configuration" link).
5. Prove the WSL agent received it. Within ~20 s (one poll):
   `wsl -d Ubuntu -- bash -c "grep -A8 -i excludeglobs ~/savelocker-test/SaveLocker/config.json"` lists `*.dmp`.
6. Prove it is **applied**:
   `wsl -d Ubuntu -- bash -c "cd ~/savelocker-test/conflict-save && echo junk > crash.dmp && echo more >> save.txt"`.
   Within ~30 s a new version from LinuxTest appears on the game page. On **Exclude patterns**, type `*.dmp` into the
   game's own field (don't save) and wait for the dry run. It must read **"Nothing in the latest save matches."**,
   meaning the file never reached the archive. Press **Discard changes**. (Or **Download** the head version under
   Versions and confirm the zip has `save.txt` and no `crash.dmp`.)
7. Audit log: `settings.default_excludes · added *.dmp`.
8. Optional (needs `sync` in step 0 so WSL runs this branch): `StagedVersion` is covered by the server suite, because
   `testenv` has no command that stages an agent update. Watch step 7.4's agent table for any machine reading
   **Update staged**.

## 6. Sign-in and Remember this browser (9c)

1. Press the lock icon in the top bar. You get the **Unlock SaveLocker** screen: an accent wash, the lead sentence, a
   **Password** field, **Remember this browser for 30 days** (checked), **Unlock**, a footer with the host and the
   version, the "Forgot it?" hint naming `Admin:PasswordHash` with a **Troubleshooting** link, and a **While it is
   locked** aside on the right. There are **no** fleet chips (agents/conflicts).
2. Enter a wrong password. The field border turns accent and "That password didn't work. It is the one set on the
   server, not your Steam or system login." appears beneath it.
3. **Remember off:** untick it and unlock. Reload the tab and you're still signed in. Close the **whole** browser,
   reopen `http://localhost:5080`, and you're signed out.
4. **Remember on:** tick it and unlock. Close the whole browser, reopen, and you're still signed in.
5. The **Troubleshooting** link works while locked (Help needs no sign-in).

## 7. Audit log, Help, What's new (9c)

1. **Audit log**: "N of M events · newest first", a search pill, **Export CSV**, **Refresh**, then machine chips
   (**All machines · N**, **WinTest**, **LinuxTest**, **Server**) and the table. Action text is accent for
   `upload.conflict` / failures / `admin.lockout`, and dim otherwise.
   - Type `backup`: only the backup rows remain, and the count updates.
   - Pick **LinuxTest**: only its rows. Pick a combination that matches nothing: "Nothing matches that filter".
   - **Export CSV** downloads only what is shown.
   - With more than 200 events, the sub-line adds "the newest 200 only — older events are not searched".
2. **Help**: "17 articles · ships with the console, works offline" with search on the right. There's a 240 px article
   card (the current article in soft accent) beside the article. `h2`s are faint uppercase eyebrows, `code` is a small
   chip, and a blockquote has an accent rule. Search `snapshot` finds **Database backups (snapshots)**.
3. **What's new**:
   - The head reads "console on v0.5.13-test", with the dev-build banner under it.
   - The three newest releases are shown in full, each with its date and a **Running** chip on the one this build
     descends from.
   - Below: **Release history** (every release, scrollable; click a version and its notes open in full above, with
     **Close**) beside **Agent versions**. Agent versions has a row per machine: agent version, the installer hosted
     for its platform, and **Current** / **Behind** / **Update staged** / **No installer hosted**.
   - Configuration → Agent updates → Edit → **Fetch from GitHub** for Windows sets the latest release. Then What's
     new's head says "v… available" with an **Update available** chip when that tag is newer than the console, or
     **Up to date** when it isn't.
4. Tab through each page: every control shows the 2 px accent focus ring.

## 8. Themes

Configuration → Appearance → **Light**, then walk steps 1, 4, 6 and 7 again. Chips, meters and banners should all
stay readable, and nothing should be a hard-coded dark colour. Switch back to **System** afterwards.

## 9. Clean up

`.\tests\testenv.ps1 clean`.
