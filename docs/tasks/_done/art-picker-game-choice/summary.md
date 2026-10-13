# Art picker: choose which SteamGridDB game to pull from

Shipped 2026-10-12 in branch `art-picker-and-linux-agent-secrets` (PR pending). Moved to `_done/`;
the original backlog note is preserved below.

---

Not started. Moved out of `Backlog.md` on 2026-10-01; the notes below are as they stood there.

Options come from the first name match, so a game whose name matches the wrong entry offers that entry's art with no way out short of renaming the game. A search box in the picker (`search/autocomplete`, remember the chosen id per game) would close it.

---

## As built

- `Game.SteamGridDbId` + `Game.SteamGridDbName` (migration `AddGameSteamGridDbSource`), nullable —
  null means "automatic first-name-match", so every existing game is unchanged.
- New admin routes: `GET /api/games/{id}/art/source` (`ArtSourceDto { sgdbId, name, automatic }`),
  `PUT .../art/source` (`{ sgdbId }`, null clears; the id is validated against SteamGridDB's
  `games/id/{id}` and the *server's* name is stored, never the caller's text), and
  `GET /api/games/{id}/art/search?q=` (`SgdbGameDto[] { id, name, year? }`, empty query answers
  without asking SteamGridDB; the query is capped at 100 chars and results at 20).
- The options listing, *Refresh art* and the missing-art backfill all use the pinned id when set.
- Console `ArtPicker.tsx`: a source row above the strips ("Automatic match: …" / "Chosen: …") with
  **Change** (search + pick) and **Use automatic match**; both strips remount from page 0 when the
  source changes.
- `tests/sgdb-stub.py` returns three candidates per query so the manual stub shows the switch;
  `run-console-security-tests.ps1` grew 12 checks (its stub now serves `games/id` and per-id art for
  the chosen entry) — 376/376, mutation-relevant asserts included. The PR review then added two more
  (the startup backfill filling from a pinned source) — 378/378, and cached the picker's source
  lookup, since it had been asking SteamGridDB again on every open.
