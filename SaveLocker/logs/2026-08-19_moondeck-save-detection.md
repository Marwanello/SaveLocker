# 2026-08-19 — MoonDeck-streamed shortcuts invisible to save detection (Cyberpunk 2077 and others)

**Status: DONE** — root-caused, fixed, and verified on the real Deck. Branch
`claude/steam-deck-save-detection-68b6c5`, no task file (investigation requested live).

## The report

Two related complaints about the maintainer's own Deck: some games "I'm sure have save paths" are
detected unreliably, and some installed games — Cyberpunk 2077 named specifically — are not picked
up by `scan` at all.

## Investigation

Built and deployed a `0.5.10-test` tarball via `tests/testenv.ps1 build`/`up -Only deck`, beside the
real installed agent (state at `~/savelocker-test-state`, `~/.local/share/SaveLocker` never touched
— see [[Gotchas]] → *`tests/testenv.ps1`*).

`savelocker scan` against the real Deck: **194 candidates, ~16 resolved a save dir.** Every
`SteamShortcut` candidate for Cyberpunk 2077 came back `(save dir unknown)`. `savelocker doctor`
confirmed why: `compatdata/3833743458` (the shortcut's AppID) does not exist — the shortcut has
never created a Proton prefix.

Dumping the raw `shortcuts.vdf` bytes around the Cyberpunk entry showed its `Exe` is
`/home/deck/homebrew/plugins/moondeck/python/moondeckrun.sh` — not the game. This is a **MoonDeck**
(Decky game-streaming plugin) shortcut: it launches MoonDeck's own streaming client, which tells a
remote host to start the real game and streams the video back. `LaunchOptions` carries
`MOONDECK_STEAM_APP_ID=1091500` — Cyberpunk 2077's real Steam AppID.

Searching every `compatdata/*` prefix on the Deck for Cyberpunk-shaped folders found real save data
— autosaves as recent as 2026-08-10, nine days before this session — sitting **orphaned** under
`compatdata/1091500`: a Steam-Store install of Cyberpunk that has since been uninstalled (no
`appmanifest_*.acf` anywhere on the machine), left behind because Steam does not clean up compatdata
on uninstall.

**Root cause:** a MoonDeck shortcut's `Exe` launches MoonDeck's own script, never the game, so Steam
never creates a Proton prefix for the shortcut's own AppID. When the same title is (or was) ALSO
installed locally under Proton, its real save data lives under a completely different AppID that
nothing in discovery ever tried.

Scanning all 134 shortcuts on the Deck for `moondeckrun.sh` found **11 MoonDeck shortcuts**. Of the 9
naming a `MOONDECK_STEAM_APP_ID`, only Cyberpunk 2077's (`1091500`) is a real, small-integer Steam
AppID with an actual local prefix — the other 8 (Minit, Forza Horizon 2, Forza Horizon (MoonDeck),
HITMAN 3, Marvel's Guardians of the Galaxy, Animal Crossing: New Horizons, Metroid Prime 4, Moving
Out) carry 19-digit values that are not real AppIDs and correctly resolve nothing, before and after
the fix below.

> [!important] A second, independent bug found in passing
> Doctor's raw (undeduped) shortcut listing showed the SAME game name under TWO different AppIDs for
> six titles on this one Deck: Metal Gear, Minit, Moving Out, Animal Crossing: New Horizons, HITMAN 3,
> Waydroid — `shortcuts.vdf` genuinely holds duplicate entries, most plausibly from MoonDeck- or
> Steam-ROM-Manager-style tooling re-creating a shortcut instead of updating the one already there.
> `LinuxGameScanner`'s final dedupe (`GroupBy(NormalizeName).OrderByDescending(has-a-save-dir)
> .ThenBy(HasSteamCloud).First()`) already prefers whichever duplicate resolves a save dir when
> exactly one does — but when NEITHER resolves, which is the common case since most duplicates are
> stray leftovers, the pick falls to an unexplained insertion-order tie-break, and nothing told the
> user two shortcuts existed at all. This is the shape of "I'm sure it has a save path but detection
> isn't reliable": SaveLocker can be silently watching a dead prefix while a better duplicate sits
> unexamined, with no diagnostic surface saying so.

## Fix

- **`SteamShortcuts.cs`** — parse `MOONDECK_STEAM_APP_ID=<digits>` out of a shortcut's
  `LaunchOptions` (`SteamShortcut.MoonDeckAppId`). Deliberately not a full tokenizer: other MoonDeck
  values are single-quoted and may contain spaces
  (`MOONDECK_LINKED_DISPLAY='LG Electronics LG TV (1600x900 mm)'`); an AppID is always a bare digit
  run after the marker, so finding the marker and reading digits is sufficient and immune to how
  neighbouring values are quoted.
- **`LinuxGameScanner.SuggestSaveDirAsync`** — when a shortcut's own compatdata prefix does not exist
  (or does not resolve a save dir), and it names a MoonDeck AppID whose compatdata DOES exist, try
  resolving there too. `installDir` is deliberately `null`, NOT the shortcut's `StartDir` — that is
  MoonDeck's own script directory, not the game's, and passing it as `<base>` would resolve template
  paths into a directory that has nothing to do with the game.
- **`Doctor.cs`** — when a shortcut has no prefix, name the MoonDeck-derived one and say whether it
  resolves: a dead-end "launch it once through Steam" becomes "✓ but MoonDeck streams appid X, which
  DOES have a local prefix: Y". Also added a duplicate-AppID note for any shortcut name backed by more
  than one AppID — a **note**, not a `Problem`, since scan's dedupe often still lands on the right
  answer and failing doctor's exit code over a mere duplicate would report a working Deck as broken
  (same reasoning as the pre-existing Heroic split-prefix note).

## Verification

`tests/linux/run-linux-tests.sh`: two new fixture entries (`make-fixtures.py`, `manifest.yaml`) — a
MoonDeck shortcut whose own AppID has no prefix but whose `LaunchOptions` names one that does, plus a
stray dead duplicate of the same name with no `LaunchOptions` at all (the exact shape found on the
real Deck). Seven new checks, all green:

```
scan finds the MoonDeck shortcut
scan resolves save via MOONDECK_STEAM_APP_ID
the resolving MoonDeck row wins over its dead duplicate
only one MoonDeck Streamed Game row survives the dedupe
doctor points at the real local prefix MoonDeck streams
doctor names the actual prefix path
doctor notes the duplicate MoonDeck shortcut
```

**221/223** total. The two failures are pre-existing and unrelated (Decky "no plugin installed"
messaging in `plugin-update`/`doctor` — neither code path this change touches); flagged as a separate
follow-up rather than fixed here, to keep this change scoped to detection.

**On the real Deck** — rebuilt and redeployed the test build:
- `scan` now resolves Cyberpunk 2077 to
  `compatdata/1091500/pfx/drive_c/users/steamuser/Saved Games/CD Projekt Red/Cyberpunk 2077` — the
  real, previously-orphaned save folder, autosaves and all.
- `doctor` explains why, and confirms the other 8 MoonDeck shortcuts still correctly resolve nothing
  — their `MOONDECK_STEAM_APP_ID` is not a real local AppID, so nothing changed for them.
- Candidates resolving a save dir: 16 → 25.
- All six duplicate-shortcut-name notes appeared, matching what was found by hand: Metal Gear, Minit,
  Moving Out, Animal Crossing: New Horizons, HITMAN 3, Waydroid.

## Notes

> [!note] Streaming is not syncing
> Even with the save path found, SaveLocker's pre-launch/post-exit sync still cannot hook a
> MoonDeck-streamed session automatically — the actual gameplay runs on a different machine, and the
> Steam wrapper only ever sees the shortcut's OWN (dead) AppID when Steam launches it. What this fix
> buys is **discovery**: the user can now `add-game --dir` the real path for manual push/pull or the
> folder watcher, which matters most for a title also played locally sometimes, and for recovering
> what is sitting there right now before it is lost to retention or a wipe. Doctor's pre-existing
> "saves written now are NOT being backed up" mismatch warning is not a false positive for this
> shape — it is honestly true for as long as a session is actively streamed rather than run locally.

> [!warning] Deck IP had moved since CONTEXT.md was last written
> `tests/testenv.local.ps1` (gitignored, authoritative) has `deck@192.168.0.103` /
> `http://192.168.0.124:5080`; [[CONTEXT]]'s Deck-handoff section still said `192.168.68.67` from a
> different LAN. Corrected there — another instance of the vault-drift trap in [[Gotchas]] → *Vault
> hygiene*.

**Deliberately not done:** a smarter dedupe tie-break for the duplicate-shortcut-name case (e.g.
preferring whichever duplicate's compatdata folder merely exists, even with no save dir resolved
yet). Doctor's note is the safer, additive fix — it hands the user the fact rather than guessing,
matching the standing rule from `logs/2026-08-09_heroic-detection.md`: a wrong guess that looks more
convincing than the truth is worse than an admitted unknown. The note is also headless-only —
nothing in the agent-ui Add Games view or the Game Mode UI surfaces it yet, since neither reads
`doctor` output at all; if this becomes a common complaint, that is the next lever, not a cleverer
tie-break.
