# Household accounts — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). No plan
file yet. **Priority: Medium.** Large: it changes the data model and the auth model.

---

## The problem

One SaveLocker server holds **one person's** saves. A `Game` is server-wide and every machine key is
fleet-scoped by design ([[Decisions]] → "A machine key is fleet-scoped"). In a household where two people
play the same game on their own PCs, both PCs join the same `Game` by canonical name, and their two different
playthroughs collide as conflicts on every push. The only workaround today is a second server container on
another port.

## What a competitor does

Hoard (AGPL — ideas only): the self-hosted server has **users**. `hoard-admin user create <name> [--admin]`
and the web panel's *Users* tab create accounts; each device token belongs to a user; saves and the
content-addressed storage are per user ("every file is stored once per user"). The Unraid template creates the
first admin from two environment variables. See `crates/hoard-admin/src/commands/user.rs`,
`crates/hoard-server/src/routes/cas.rs`, `Unraid.md`.

## Decisions to make first

1. **What a user owns.** Option A: every `Game` belongs to one user (simple; two people with one game get two
   games). Option B: a `Game` is shared and each user has their own version lineage (head, conflicts) under it
   (shares art and settings, more schema work). A is the likely answer.
2. **Who a machine belongs to.** One user per machine (a PC per person), or per OS account on a shared PC.
   The Deck is one Steam account, so one user.
3. **Admin vs member.** Members see only their games in the console; the admin sees everything (fleet health,
   updates, backups).
4. **Migration.** Every existing row becomes the first (admin) user's.

## Proposed shape (once the decisions are made)

1. `User` entity + migration; `Machine.UserId`; `Game.UserId` (Option A); every agent route scoped by the
   machine's user; the enrollment join link carries the user.
2. Console sign-in per user (the session model in `AdminAuth.cs` gains a user id); a Users card in Config.
3. Agent and plugins need no change beyond the user being implied by their key.
4. A security pass: a machine key must never read another user's game (new section in
   `run-server-bugbounty-tests.ps1`).

## Touch points

`src/Server/Data/Entities.cs` + a migration, `SyncService.cs` (every query), `EnrollmentService.cs`,
`AdminAuth.cs`, `Program.cs` (route groups), `openapi.json` + both `api-types.ts`, `web/` Config and
sign-in, [[Decisions]], [[API Reference]].

## Verify

Two users, one game name each, pushed from two machines → two games, no conflict; a member's key gets 404 on
the other user's game; existing single-user installs migrate with nothing visible changed.
