# Session summary — 2026-10-12 — Emulator saves Group E and the Xenia profile/DLC design (PR #68)

Standalone: the facts below do not need the rest of the vault.

**PR:** #68 on `Marwanello/SaveLocker`, *Emulator saves Group E: Cemu, Azahar, Xenia, Xbox 360 and 3DS names, title-ID
folders*. Branch `emulator-saves-group-e`, base `main`. Open at the time of writing.

## What was asked

1. Implement the next emulator-saves group (Group D had merged as #65, so Group E: Cemu, Azahar, Xenia), with testenv
   verification steps and a technical and plain explanation, on a PR to the fork.
2. Research whether any emulator ties a save to a user.
3. Explain the IDs shown as names (`3DS 00054000`, `Wii U 10145D00`, `3DS 0016E300`).
4. Build the Xenia fixes; for the profile one, plan first: copy the whole profile (no other saves) and add a DLC option.

## Decisions taken with the maintainer

- Group E, captured from the Deck (read-only SSH) and the PC, plus EmuDeck's scripts and the emulators' source.
- A Citra save and an Azahar save of one game are **one game**.
- A **bundled title table where licensed** (libretro-database, CC BY-SA 4.0): Xbox 360 by title ID, 3DS by ROM product code.
  No Wii U source exists; the only 3DS title-ID list (3dsdb) has no licence.
- Xenia: build device saves and the named-profile layout; design the whole-profile copy and DLC before building.

## What was built

| Emulator | A game is | Scope | Name |
|---|---|---|---|
| Cemu (Wii U) | `usr/save/00050000/<low>` | `<low>/**` | Cemu's title cache, `meta.xml`, else `Wii U <ID>` |
| Azahar (3DS) | `sdmc/Nintendo 3DS/…/title/00040000/<low>/data` | `<low>/data/**` + states `<program ID>.*` | ROM product code via No-Intro, file name, else `3DS <ID>` |
| Xenia (Xbox 360) | saved games in `content/<XUID>/<TID>`, `content/<name>/<TID>` or `content/<TID>`; device saves in `content/0000000000000000` (key `device`) | `<TID>/00000001/**` + headers | title ID via Redump/No-Intro, else `Xbox 360 <ID>` |

Plus `Ncch` (3DS ROM header), the title table rows, Add games chips, help, testenv fixtures. Unit **474**; Azahar's and Xenia's
scopes mutation-checked in two-machine tests (a wider scope deletes the installed game / DLC on pull).

## What was found

- No emulator checks a save's owner on load. Cemu (`user/<account>`, first account `80000001`), RPCS3, shadPS4, Vita3K and
  Xenia (`content/<XUID>`) bind by folder only. Xenia XUIDs are random per profile; some games may store the XUID inside the
  save, so one profile copied to every machine is the safe setup.
- Captures: the Deck's three real 3DS saves and 11 ROM headers (program IDs match the folders), Cemu/Azahar AppImages with
  data on the SD card, Xenia's two layouts; the PC's real Xenia *Forza Horizon* saves. `00054000` is Super Mario 3D Land,
  `10145D00` Mario Kart 8 (fixture), `0016E300` an unidentified Deck save with no ROM.
- The Xenia profile is `content/<XUID>/FFFE07D1/00010000/<XUID>/` (encrypted `Account`, per-game `.gpd`) + header, ~210 KB.
  DLC is always under XUID 0; Forza Horizon's is 1.0 GB, over the 500 MB upload cap.

## Phase 5c — designed, waiting on the maintainer

1. **Profile first:** each Xenia profile is its own Add games entry, scoped to its XUID (`FFFE07D1/00010000/<XUID>/**` + header).
   A machine without it sees it *On the server* and pulls it; sign in once, and every game's saves then land in the same folder.
2. **DLC per game, opt-in:** its own entry (`<TID>/00000002/**` under XUID 0), added from Add games or the game page later;
   needs a larger server cap for DLC.

Open questions: profile as its own entry or a folder of each game; DLC as its own entry and whether title updates come with it;
the DLC cap and its default; the gamertag from the decrypted `Account` or the XUID only.

## Not verified

The Group E testenv pass, and the real-game checks (Group H: Cemu, Azahar, Xenia).

**Phase 5c, maintainer's calls (same day):** the profile and DLC as folders of each game, a separate 2 GB cap for DLC, the
gamertag decrypted. Two consequences recorded in the plan and raised before building: one game's pull restores the profile as
that game last pushed it (other games' newer achievements/settings lost on that machine, and `SaveFolderClaims` needs an
exception); DLC in every version makes Forza Horizon ~1 GB per version (~7 GB kept, 1 GB per pull).
