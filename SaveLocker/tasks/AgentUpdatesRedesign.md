# Task — Agent Updates card: condense to informational + edit wizard

**Created:** 2026-08-16

**Target:** `web/src/components/ConfigView.tsx` (Agent Updates card, `~line 306` on), likely a new
`AgentUpdatesModal.tsx`/similar; possibly `src/Server/Services/AgentInstallerService.cs` and
`Program.cs` route additions for a bulk-fetch-all endpoint and a GitHub hash-verify endpoint.

**Goal:** the card currently manages three packages (win-x64, linux-x64, decky-plugin) inline —
upload/fetch/delete controls sit directly on the card and it has grown cluttered. Redesign to:

1. **Card becomes mostly read-only status**: for each of the 3 packages, show current hosted version,
   source (manual upload vs. last GitHub fetch), and date. No inline action buttons on the card itself
   beyond a single **Edit** button that opens a modal/wizard.
2. **Edit modal**: one **"Fetch latest for all packages"** action — shows all 3 packages pre-checked
   in a list, user can uncheck any before confirming; only checked packages get fetched from GitHub.
   Below/alongside that, keep the existing per-package manual-upload and fetch-individually actions,
   just moved into the modal instead of the card.
3. **Hash verification display**: for each package, show whether the hosted binary's SHA-256 (already
   computed server-side — see `AgentInstallerService.BackfillDigestAsync` / `AgentInstallerStatus`
   for what's already tracked) matches the hash published in the SaveLocker GitHub repo's release for
   that version. This needs a server-side check (fetch the release's published checksum file/asset
   digest from GitHub and compare) — reuse whatever HTTP client pattern `fetch-github` already uses
   in `Program.cs` (~774) rather than inventing a new one.

**Motivation (maintainer, 2026-08-16):** "grown cluttered with 3 packages to maintain" — wants the
default view to be glanceable status, with editing behind one explicit action.

---

## Before starting

Read the current card fully (`ConfigView.tsx` — search `Agent updates`) and `AgentInstallerService.cs`
end to end; this task description is scoped from a distance and the exact shape of
`AgentInstallerStatus` / existing upload-fetch-delete routes needs confirming before designing the
modal's data flow. Check `src/Server/Program.cs` `admin.MapPost("/admin/agent-installer"...)`,
`.../fetch-github`, `.../delete` (~707-800) for what already exists per-platform — the bulk-fetch
action is very likely "call fetch-github for each checked platform slot", not new server logic,
*unless* hash verification requires a new endpoint (it does — see point 3).

## Decisions to make first

1. Where does "the GitHub repo's published hash" come from for verification — the release's own
   `SHA256SUMS-*.txt` asset (mentioned in `CONTEXT.md`/`Backlog.md` for the Linux tarball), a
   per-asset digest from the GitHub API, or something else? The Decky plugin's index already carries
   a hash (`store/plugins`, per `CONTEXT.md`'s "regenerates `store/plugins` with the artifact's
   SHA-256") — that may be the easiest one to verify first; the two agent installers may need the
   `SHA256SUMS-*.txt` release asset instead. Confirm before implementing rather than guessing per
   platform.
2. Does the modal need its own loading/error states independent of the card (likely yes — a
   multi-package fetch can partially fail).

## Verification

- Manual, in the browser: card renders informational-only, Edit opens the modal, bulk-fetch with one
  package unchecked only touches the checked ones, hash verification shows a clear match/mismatch/
  unknown state per package.
- If any new server route is added, add it to `src/Server/openapi.json` + regenerate
  `web/src/api-types.ts`.

**Stop and report after this task.**
