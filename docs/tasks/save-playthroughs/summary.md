# Playthroughs (named save forks) — summary

Asked for 2026-10-16 by the maintainer, high priority. **Planned 2026-10-22: see `plan.md` and
`implementation-grouping.md` beside this file** (the questions below are answered at the end of `plan.md`). This is the idea and the questions the plan
has to answer.

---

## The idea

A game's save can be **forked into a named playthrough**: "Main", "Hard mode run", "Sara's island". Each playthrough is
its own line of versions on the server. A device follows one playthrough of a game at a time and syncs only with it.
**Switching** puts that playthrough's latest save in the game's folder (after the one in use is pushed to its own
playthrough), so the user can jump between independent saves in a couple of clicks, on any device.

Two uses:
- **Several runs of one game** that would otherwise overwrite each other: a new game+, a challenge run, trying a branch of
  the story without losing the main save.
- **Several people on one PC or Deck** who each want their own save of the same game, where the game itself has one save
  slot (Animal Crossing's island is one per console).

## What it touches (first look)

- **Server:** a playthrough per game (name, created from which version, owner?); `SaveVersions` gain a playthrough;
  the head, conflicts, leases, retention and the per-file delta baseline become per playthrough. "Fork from this
  version" on the console's Versions list.
- **Agent:** each tracked game records the playthrough it follows here; pull and push use it. A switch is: refuse while
  the game runs, push the current playthrough, pull the target one, record the switch. The launch check names the
  playthrough ("Playing *Hard mode run*").
- **UI:** a playthrough picker on the game page (agent and console), the Deck's Game Mode list, and the playthrough's
  name on every sync, conflict and version line, so a save is never put in the wrong one silently (the lesson of the
  2026-10-15 "which emulator" work).

## Questions for the plan

- Are playthroughs per person (tied to a machine user or a SaveLocker profile) or just named lines anyone can pick?
- Does switching keep the outgoing save locally too, for offline switching, or always go through the server?
- A device that follows "Main" while another pushes "Hard mode": no conflict, but should it be told?
- Emulator profiles (Xenia's profile, a Switch emulator's user) already separate some saves: does a playthrough map onto
  those where they exist, or always replace the whole folder?
- Retention: does each playthrough keep its own version count?
- Delete and merge: can a playthrough be deleted, renamed, or made the main one?
