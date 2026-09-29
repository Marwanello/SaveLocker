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

## 1. Backups — the page and Back up now (9a, reworked 2026-09-29)

1. The tab strip reads **Games · Configuration · Audit log · Backups · Help · What's new**. Click **Backups**.
2. The head reads **Backups**, "the database and every game's latest save · every Sunday at 03:00 UTC · keeps the newest 7",
   with a chip (green **Last just now**: `up` took a catch-up backup on the fresh volume) and **Back up now**.
3. Four tiles:
   - **Backups**
   - **On disk** (`/data/backups`)
   - **All save versions**, with "only each game's latest is backed up"
   - **Next run**, reading Sunday 03:00 UTC **in your local time** (e.g. **Sun 05:00** in UTC+2), with "in Nd Nh · 03:00 UTC" under it
4. The table has File (`….zip`), Taken ("… · HH:MM UTC"), Size, **Holds** ("Database + latest saves"), Reason, and
   **Download** / **Restore** on every row.
5. **Back up now**: the toast reads **Backup written. N KB.**, and a new `savelocker-YYYYMMDD-HHMMSS-manual.zip` row
   appears with an amber **Manual** chip. The timestamp in the name is UTC.
6. **Download** it, then check what's inside. Conflict Game has several versions, but only **one** archive must be in
   the zip:
   ```powershell
   $f = (Get-ChildItem "$env:USERPROFILE\Downloads\savelocker-*-manual.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
   Add-Type -AssemblyName System.IO.Compression.FileSystem
   [IO.Compression.ZipFile]::OpenRead($f).Entries | Select-Object FullName, Length, CompressedLength
   ```
   Expect `savelocker.db` (its CompressedLength much smaller than Length), `manifest.json`, and exactly one
   `archives/<gameid>/<versionid>.zip`, the Latest shown on the game page.
7. Go to **Audit log**. There are `backup.manual` and `backup.download` rows, each naming the file.
8. Security, from PowerShell, once a password is set (step 4.6):
   - `curl.exe -s -o NUL -w "%{http_code}" http://localhost:5080/api/admin/backups/<file>` returns `401`.
   - `curl.exe -s -D - -o NUL -H "X-Admin-Password: <pw>" http://localhost:5080/api/admin/backups/<file>` shows
     `Cache-Control: no-store`.
   - `.../api/admin/backups/..%2Fsavelocker.db` returns `404`.

## 2. The schedule, in UTC (9a + 9b)

1. **Configuration → Defaults & maintenance → Scheduled backup** is on and reads "The database and every game's latest
   save, zipped, [weekly] on [Sunday] at [03:00 UTC], keep [7]".
2. Change it to **Wednesday** and **04:00 UTC**. The toast reads "Backups on: every Wednesday at 04:00 UTC, keeping 7."
   **Backups → Next run** reads Wednesday 04:00 UTC in your local time, with "· 04:00 UTC" under it.
3. Switch to **daily**. The day picker disappears and Next run is within 24 h. Switch back to weekly.
4. Switch it **off**. Next run reads **Off**. Switch it on again.
5. Set keep to **2** and press Back up now three times. Only 2 rows remain. Set keep back to 7.
6. The Audit log has `settings.backup` rows such as "scheduled on, weekly on Wednesday at 04:00 UTC, keep 7 (was …)".

## 3. Restore from the Backups page (new)

1. Note the newest backup (call it **B**). Then change something visible. For example, add a game with **+ Add game**
   named `After-B`, and on WSL make a new save:
   `wsl -d Ubuntu -- bash -c "echo after-b >> ~/savelocker-test/conflict-save/save.txt"`. Wait until Conflict Game shows
   a new Latest.
2. Simulate lost save files. Delete Conflict Game's archives inside the container:
   `docker exec savelocker-test sh -c 'ls /data/archives'`, then
   `docker exec savelocker-test sh -c 'rm /data/archives/<that game folder>/*'`.
3. On **Backups**, press **Restore** on **B**. It expands into a sentence saying it replaces the whole database, a
   **Before restore** backup is taken first, and you may have to sign in again. Confirm **Restore B**.
4. The toast reads "Restored B. 1 save put back; the state before it is in …-before-restore.zip". A **Before restore**
   row appears at the top.
5. Check the state:
   - **Games**: `After-B` is gone.
   - Conflict Game's Latest is the one from B, and **Download** on it works (the archive was put back).
   - `docker exec savelocker-test sh -c 'ls /data/archives/<folder>'` shows the file again.
6. The Audit log has a `backup.restore` row naming B and the safety backup.
7. **Undo**: press **Restore** on the **Before restore** row. `After-B` is back. (The WSL save from step 1 was in the
   database's history, so its version row returns. Its file was deleted in step 2 and wasn't in B, so that version
   lists but won't download.)
8. Garbage is refused: `docker exec savelocker-test sh -c 'echo junk > /data/backups/savelocker-20000101-000000-manual.zip'`,
   reload, **Restore** it. A red toast "Nothing was restored: …" appears and no Before restore row is added. Then delete
   the file with `docker exec … rm`.

## 3b. Before-upgrade backup

1. Rebuild the console at a different version, keeping its volume:
   `.\tests\testenv.ps1 build -Only console -Version 0.5.99-test`, then `down`, `up -Only console`, `up -Only windows`,
   `up -Only linux`.
2. `status` shows the console `v0.5.99-test`. **Backups** has a new `…-before-upgrade.zip` row. Its Holds column reads
   **Database only**: it runs before migrations.
3. Prove it predates the new start:
   ```powershell
   $f = (Get-ChildItem "$env:USERPROFILE\Downloads\*-before-upgrade.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
   Add-Type -AssemblyName System.IO.Compression.FileSystem
   $z = [IO.Compression.ZipFile]::OpenRead($f); [IO.Compression.ZipFileExtensions]::ExtractToFile($z.GetEntry('savelocker.db'), "$env:TEMP\bu.db", $true); $z.Dispose()
   python -c "import sqlite3,sys;print(sqlite3.connect(sys.argv[1]).execute('select Value from Settings where [Key]=?',(sys.argv[2],)).fetchone())" "$env:TEMP\bu.db" Server:LastStartedVersion
   ```
   It should print `('0.5.13-test',)`, the old build.
4. `down` + `up -Only console` again at the same version: **no** second before-upgrade row.
5. Rebuild at the normal version (`build -Only console`, `down`, `up`) before continuing.

## 3c. Encryption at rest

1. Configuration → **SteamGridDB artwork**: paste a real key and **Save key** (or run `testenv up` with the stub from
   *Testing artwork* in Build and Run). The chip reads **Key set** with the last 4 characters.
2. Prove it isn't stored in plain text. Copy the database out and read the row:
   ```powershell
   docker cp savelocker-test:/data/savelocker.db "$env:TEMP\live.db"
   python -c "import sqlite3,sys;print(sqlite3.connect(sys.argv[1]).execute('select Value from Settings where [Key]=?',(sys.argv[2],)).fetchone())" "$env:TEMP\live.db" SteamGridDb:ApiKey
   ```
   It should print `('enc:v1:…',)`, never the key itself. (A copy taken while the server runs can miss the newest
   writes, which sit in the WAL; save the key a minute before copying.)
3. `docker exec savelocker-test ls /data/keys` shows a `key-….xml`. That's the key ring, and it's not in any backup (a
   downloaded zip has only `savelocker.db`, `manifest.json` and `archives/…`).

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

1. Press the lock icon in the top bar. You get the **Unlock SaveLocker** screen with an accent wash, the lead sentence,
   a **Password** field, **Remember this browser for 30 days** (checked), **Unlock**, a host · version footer, the
   "Forgot it?" hint with a **Troubleshooting** link, and a **While it is locked** aside. **The top bar shows only the
   logo and version: no tabs, no Sync all, no bell, no lock.** There is no fleet status anywhere.
2. Enter a wrong password. The field border turns accent and "That password didn't work. It is the one set on the
   server, not your Steam or system login." appears beneath it.
3. **Remember off:** untick it and unlock. Reload the tab and you're still signed in. Close the **whole** browser,
   reopen `http://localhost:5080`, and you're signed out.
4. **Remember on:** tick it and unlock. Close the whole browser, reopen, and you're still signed in.
5. The **Troubleshooting** link works while locked (Help needs no sign-in). The top bar then shows only
   **Back to sign in**, and pressing it returns to the lock screen.

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
