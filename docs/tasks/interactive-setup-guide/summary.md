# Interactive setup guide in the console — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **Interactive setup guide in the console.** Raised 2026-08-14 alongside the savelocker.com plan. A first-run walkthrough that takes a new user from an empty server to a syncing game, in the console itself rather than in prose: mint an enrollment file → wait for the machine to appear → add a first game → confirm a push landed, each step self-checking against live state (Machines, GameState) so it advances on its own and cannot claim a step is done when it is not. Two reasons this is not documentation: the marketing site's Get Started has to end *somewhere*, and the natural place is "open the console, it takes it from here" — and the Deck's launch-option step (`savelocker run -- %command%`) is the single most-missed action in the whole flow, with nothing today that notices it was skipped. Re-runnable from Help, and resumable — a user enrolls the Deck days after the PC. Reuses the Help KB shell (`web/src/help/`) for the article surface; the checks are `/api/overview` + machines. Deck Game Mode already has a "Next step" card (`Ui/UiApp.cs`) — same idea, wider scope.

---

## Competitor research (2026-10-18) — widen to "install to first sync"

**Priority: High** (was Medium). From [[logs/2026-10-18_competitor-gap-analysis]]: setup and distribution is the
area where SaveLocker scores lowest (2 of 5) against Hoard, OpenSave and EmuSync.

- **Out of date above:** step one is no longer "mint an enrollment file" — enrollment uses **join links** since
  PR #76.
- **Unraid Community Apps template.** Hoard ships `templates/hoard.xml` and `ca_profile.xml`: port, data path,
  PUID/PGID 99/100, and **admin username + password as template fields**, so the first start creates the account
  with no terminal; its `Unraid.md` covers both the Apps-tab install and the manual template. SaveLocker has only
  `docker-compose.unraid.yml`. Add `templates/savelocker.xml` (port 5080→8080, `/mnt/user/appdata/savelocker`,
  `SAVELOCKER_UID`/`GID` 99/100, the agent-update repo variables, an optional first admin password), then submit
  it to Community Applications.
- **First run with no admin password.** Today the console is open until a password is set. BlockSlot's web app
  has a `first_run` state that asks for the admin account before anything else (`server/blockslot_server/webapp.py`).
  Make the guide's first step "set the admin password", then the join link, then the machine appearing, then the
  first game and the first push — each step checking live state, as planned above.
- Keep the Deck's launch-option step (the most-missed one) and the Decky plugin install in the guide.
