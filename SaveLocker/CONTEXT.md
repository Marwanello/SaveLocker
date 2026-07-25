# SaveLocker — Session Context

**What:** Self-hosted Windows game save sync. Hub-and-spoke: C#/WinForms tray agent on each PC syncs saves through an ASP.NET Core server (Docker on unRAID). React admin dashboard + embedded React agent UI.

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** main

**Current released version:** **v0.4.0** (tagged 2026-07-25). GHCR package `savelocker` is **public** (see `Gotchas.md`).

Shipped-feature detail: `logs/shipped-2026-07.md` + `logs/sessions.md`. Open work: `Backlog.md`.

---

## Status (2026-07-23)

| Area | State |
|------|-------|
| Tray agent (WinForms + React/WebView2) | ✅ done |
| Game scanning (Steam VDF + Ludusavi) | ✅ done |
| Server (REST API, EF/SQLite, leases, conflicts) | ✅ done |
| Admin dashboard (React + Tailwind, baked into Docker) | ✅ done |
| Agent auto-update (version, silent relaunch, installer persistence) | ✅ verified on device |
| Scheduled GitHub installer auto-poll | ✅ dashboard-configurable |
| Per-game exclude globs + 200 MB upload cap | ✅ v0.1.4/0.1.5 |
| CI/CD (push → Docker → GHCR; tag → GitHub Release) | ✅ done |
| Console Help KB (14 articles, search, deep-links) | ✅ done |
| Save-in-use safety (settle gate before auto-push) | ✅ built and device-verified |
| Cross-OS round-trip in CI (Windows agent ↔ Linux agent) | ✅ done |
| Runtime: .NET 10 (LTS) | ✅ merged PR #2 |
| Linux agent (all 6 phases) | ✅ done — PRs #1, #4–#6 |
| Local agent API hardening + fleet key rotation | ✅ done |
| Deck path setup (agent UI browser, console inline edit, scan candidates) | ✅ done — v0.3.0 |
| Console versioning + release notes + version skew warnings | ✅ done — v0.3.2 |
| Conflict handling (Tiers 0–2, agent backoff, escalation, Keep both) | ✅ done — v0.3.4 |
| Deploy v0.3.4 to unRAID | ✅ done 2026-07-24 |
| Deck Game Mode UI visual refresh (theme, widgets, 1280×800 shell, Settings, sounds) | ✅ done — v0.4.0, 2026-07-25 |
| Durable lease warnings (`lease-warnings.json`) | ✅ done — fixed a real gap: Linux never surfaced them at all |
| Linux add-game streamline — Phases 1–2 (dead button, prefix browser, enroll gate, launch card) | ✅ done — PR #24, 2026-07-24 |
| Linux add-game streamline — Phase 3 (gamepad-native `savelocker ui`) | ✅ done — shipped 2026-07-24 (PR #25 + artwork PR #26); design archived to `logs/2026-07-24_linux-agent-streamline.md` |

---

## ▶ NEXT ACTION: **Verify v0.4.0 on the Deck, then deploy to unRAID**

v0.4.0 is tagged. Remaining:

- **Steam Deck:** the maintainer is testing the release build. Download the v0.4.0 tarball and
  re-run `install.sh`.
- **unRAID:** `docker compose pull && docker compose up -d` once CI has published.
- **Windows agents:** auto-update.

⚠️ **Confirm both `docker-publish` and `release.yml` triggered on the tag** — see the v0.3.4 incident
in `Gotchas.md`. If not, re-trigger manually.

**Known issue shipped knowingly:** in `savelocker ui`, Left returns to the rail only from Overview;
B works everywhere. Recorded as a stretch item in `Backlog.md` with the eight approaches already
ruled out — read it before attempting a fix.

**The Deck UI now has a dev loop** — `tests/linux/run-ui-wslg.sh` runs it under WSLg with fixtures,
scripted D-pad input and headless screenshots. See the quick-reference table.

## Open

- **Fresh Windows installer enrollment** — the wizard shipped in v0.1.7 and the upgrade path is verified. The **fresh install** (clean box, no `%PROGRAMDATA%\SaveLocker`) has never been exercised. Scenarios in `logs/2026-07-14_installer-enrollment.md`. ACL trap: verify `icacls "%PROGRAMDATA%\SaveLocker"` gives the interactive user Modify.
- **Code-sign the exe** — SmartScreen warns for unsigned installers. Explicitly deferred by the maintainer.
- ⚠️ **`%PROGRAMDATA%\SaveLocker` ACLs on a multi-user Windows box.** `api-token` and `config.json` are readable by other local users. See `Backlog.md`, medium priority.
- **Linux auto-update** — Deck users re-run `install.sh` from a newer tarball. No auto-update channel. Worth doing before there are many Deck users.
- **File-count / newest-mtime delta in conflict UI** — the server doesn't store it; would need computing at upload time. Not done; everything else in 1.2 is done.
- **Do not host v0.1.6 for auto-update** — it aborts under /SILENT. Host v0.2.0 or newer.

---

## Dev quick-reference

| Task | Command |
|------|---------|
| Run server | `cd src/Server && dotnet run` → http://localhost:5179 |
| Run dashboard | `cd web && npm run dev` → http://localhost:5173 |
| Build agent | `dotnet build src/Agent/SaveLocker.Agent.csproj --no-incremental` |
| Build installer | `.\installer\build-installer.ps1` |
| Agent integration | `.\tests\run-agent-tests.ps1` (**45** on Windows / 43 on Linux). Needs server on :5179 — and `.verify/` cleared **in the same breath** (see `Gotchas.md`) |
| Enrollment tests | `.\tests\run-enrollment-tests.ps1` (16 checks; needs :5179). Run **after** agent suite |
| Local agent API security | `.\tests\run-local-api-tests.ps1` (**27** checks, +2 when a candidate is scanned). Starts own daemon on **:5188** |
| Cross-process state | `.\tests\run-concurrency-tests.ps1` (17 checks; own server on **:5183**, own daemon on **:5189**) |
| Health tests | `.\tests\run-health-tests.ps1` (19 checks). Starts own server on :5181 |
| Hardening tests | `.\tests\run-hardening-tests.ps1` (14 on Linux / 13 on Windows; own server on :5182) |
| TOFU pin tests (TLS) | `.\tests\run-enrollment-tls-tests.ps1` (6 checks; own HTTPS server on :5443). Needs `dotnet dev-certs https --trust` |
| Linux fake-game harness | `tests/linux/run-linux-tests.sh` (27 checks; starts own server) |
| **Deck UI under WSLg** | `bash tests/linux/run-ui-wslg.sh [WxH] [flags]` — runs `savelocker ui` in a real window at the Deck's 1280×800. **Needs `-r linux-x64`** (the script does it) or SDL "isn't applicable" — see `Gotchas.md`. Validates layout/palette/type only; gamepad, gamescope and clipboard stay Deck-only |
| ↳ Deck UI flags | `--screenshot out.png` (capture + exit), `--screen status\|add\|folder\|launch`, `--gallery` (every widget in every state), `--fixtures` (fake games, so **populated** screens render), `--autoscan`, `--no-build`. Combine: `--fixtures --autoscan --screen folder --screenshot x.png` |
| Cross-OS round-trip | `tests/cross-os/crossos.ps1 -Leg author\|roundtrip\|confirm` |
| Password-hash compat | `.\tests\verify-password-compat.ps1` |

**WSL is a working test bed — use it.** Ubuntu 24.04 at `~/SaveLocker`. `dotnet` and `pwsh` are **not on a non-interactive PATH** — export first:
```sh
wsl -d Ubuntu-24.04
export DOTNET_ROOT=$HOME/.dotnet; export PATH=$HOME/.dotnet:$HOME/.local/bin:$PATH
cd ~/SaveLocker && git fetch /mnt/e/Projects/SaveLocker <branch> && git reset --hard FETCH_HEAD
cp -r /mnt/e/Projects/SaveLocker/agent-ui/dist/. agent-ui/dist/
dotnet build src/Server/SaveLocker.Server.csproj src/Agent.Linux/SaveLocker.Agent.Linux.csproj --no-incremental
bash tests/linux/run-linux-tests.sh
```
⚠️ Suites needing :5179 must use isolated `Storage__DbPath` / `Storage__ArchiveRoot`. Reusing a dirty dev DB fails 12/16 of the enrollment suite.

**Always use `--no-incremental`** — stale DLL reuse has masked changes before. Stop the running agent/server first.

**CI (`ci.yml`) runs 8 jobs on every PR:** `build-dotnet`, `build-web`, `build-agent-ui`, `docker-build`, `agent-tests-linux`, and the chained `crossos-author → crossos-roundtrip → crossos-confirm`.

### Toolchain

| Where | What |
|------|------|
| Windows | .NET SDK **9.0.315 + 10.0.301** side by side. `global.json` pins **10.0.x**. Windows PowerShell 5.1 only — scripts must stay 5.1-compatible (no `??`, no `$IsWindows`, BOM-less `.ps1` reads as ANSI — keep ASCII). |
| WSL (Ubuntu 24.04) | .NET SDK **9.0.315 + 10.0.301** in `~/.dotnet`; **pwsh 7.4.6** in `~/.local/pwsh`; **Node 22 via nvm** (`. ~/.nvm/nvm.sh`). Repo clone at `~/SaveLocker` on ext4 — never build from `/mnt/*`. |

⚠️ Inside WSL, `npm` resolves to the WINDOWS npm unless a Linux node is on PATH first — symptom: `error TS5083: Cannot read file 'C:/Windows/tsconfig.json'`. Source nvm first.

---

## Deployment

- **unRAID:** Docker on port 5080. `git push main` → Actions build → GHCR. Deploy: `docker compose pull && docker compose up -d`.
- **Tag a release:** ⚠️ **Write `web/src/releases/<ver>.md` FIRST and commit before tagging.** Add the new entry to `web/src/releases/index.ts` in the same commit. Then `git tag v0.x.y && git push origin v0.x.y`.
  - **Windows:** `SaveLocker-Agent-Setup-<ver>.exe` (Inno Setup, `windows-latest`).
  - **Linux / Steam Deck:** `savelocker-<ver>-linux-x64.tar.gz` (self-contained, **must** be built on `ubuntu-latest` — glibc forward-compat).
- **Linux install:**
  ```
  tar -xzf savelocker-<ver>-linux-x64.tar.gz
  ./SaveLocker/install.sh
  savelocker enroll --file <policy.json>
  savelocker doctor
  ```
- ⚠️ **The Linux tarball is kept as a run artifact** (14 days) — download from CI Artifacts to put any commit on a real Deck without tagging. Stamped `9.9.9-ci` so it cannot be mistaken for a release.
- ⚠️ **Check `git tag -l` before tagging** — this file once claimed v0.1.7 while v0.1.8 was already pushed.
- ⚠️ **Confirm `docker-publish` triggered on the tag** (not just the push) — see the v0.3.4 incident above.

---

## Critical gotchas (read before touching builds or paths)
- Incremental builds can silently reuse stale DLLs → always `--no-incremental`
- Stop running agent/server before building (DLL file-lock)
- Dev storage uses `localstate/` not `data/` (Windows case-collision: `Data/` = source folder)
- `dotnet` may not be on PATH in an open shell after winget install — open a new shell
- OneDrive save paths block `Directory.Move` — RestoreArchive uses file-by-file copy to `_tempDir`
- PowerShell array splatting to native exes splits strings containing `:` — always use `if/else` + long-form `"--property:Key=Value"`
- MinVer requires git access; silently fails to `0.0.0.0` on GitHub Actions Windows runners — pass `--property:Version=$v` explicitly
- `npm run gen:api` in `agent-ui/` hardcodes :5178 (the installed agent's port) — generate against a dev daemon on a free port
- Clearing only the server DB (not `.verify/`) causes ~3 agent-suite failures — clear both together
- See `Gotchas.md` for the full list with fixes

---

## Key files
| Topic | File |
|-------|------|
| Codebase map | `REPO_MAP.md` |
| System design | `Architecture.md` |
| Locked decisions | `Decisions.md` |
| Known traps | `Gotchas.md` |
| REST endpoints | `API Reference.md` |
| Dev build & run | `Build and Run.md` |
| Agent CLI | `web/src/help/cli-reference.md` (KB article) |
| Active backlog | `Backlog.md` |
| Session history | `logs/sessions.md` |
