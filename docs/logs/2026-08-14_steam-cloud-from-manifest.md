# 2026-08-14 — PCGamingWiki harvesting, and the field we were already dropping

Branch `steam-cloud-from-manifest`. Not merged, not released.

## The question

> "There is a host of data we can be harvesting for additional save game paths. Look at this
> example: https://www.pcgamingwiki.com/wiki/Breath_of_Fire_IV"

## The answer to the question as asked: no, and here is the proof

**The Ludusavi manifest IS a PCGamingWiki scrape.** Breath of Fire IV's Game data table is in the
manifest we already download, token for token:

| PCGamingWiki | `data/manifest.yaml` |
|---|---|
| `<path-to-game>\BOF4-**.DAT` (Windows) | `<base>/BOF4-*.DAT` — `tags: [save]`, `when: os: windows` |
| `<path-to-game>\SAV\*.DAT` (GOG) | `<base>/SAV/*.DAT` — `when: store: gog` |
| `<path-to-game>\english\SAV\*.DAT` (Steam) | `<base>/english/SAV/*.DAT` — `when: store: steam` |
| the two `bof4.cfg` rows | same paths, `tags: [config]` |
| Steam AppID 4249150 | `steam: id: 4249150`, `installDir: 4249150_BreathofFire4` |

All seven rows, both stores, the save/config distinction intact. 52,973 entries, 21,061 with save
paths. Building a scraper would re-derive a dataset one `GET` refreshes — against a site that 403s
automated fetches. Recorded in `Decisions.md` so it does not come back.

If save-path coverage needs to improve, the lever is **contributing upstream to the wiki**, or
working Ludusavi's own parse-failure list. Both flow back to us on the next refresh.

## What that page actually exposed

**A `cloud:` block we never parsed, while guessing the same fact wrongly.**

`ManifestLoader.ManifestGame` read `files` and `installDir` only. Meanwhile both scanners hardcoded
`HasSteamCloud: true` for anything Steam installed — "Steam titles usually have Cloud". Measured
over the manifest:

```
games with a Steam id            48,908
  ...Steam Cloud actually true   14,340
  ...no Steam Cloud              34,568
  ...no Cloud AND has save paths  7,057   <- hidden by the default Add Games view
```

Breath of Fire IV is the exact counterexample the link lands on: sold on Steam, `cloud: gog: true`,
no Steam Cloud. Hidden as already-covered when nothing covered it.

**Caveat kept honest:** those counts are manifest-wide, not weighted by what people own. Real
libraries skew newer, where Cloud is far more common, so the fleet-visible effect is smaller than
the ratio implies. The direction is what matters — the assumption is wrong more often than right.

## The encoding trap that decided the design

**The manifest never writes `false`.** A `cloud:` block lists only the stores whose wiki row is
ticked and is omitted entirely when none are — 15,465 of 52,973 entries carry one. So "no block"
and "a block without `steam`" are the same statement, and both mean no.

Residual uncertainty: a page with no cloud table at all is indistinguishable from one whose table is
all-unchecked. Both produce no block. That is why the failure direction was chosen deliberately —
a wrong "no Cloud" puts an extra game in a list the user can ignore; a wrong "has Cloud" hides a
game they own and never tells them. The former is strictly the safer error.

Hence `bool?`, with `null` = *not in the manifest at all* keeping the old assumption. The change
only overrides where real data exists, so nothing regresses for unknown games.

## Verification

Against the **real** manifest, not fixtures: Breath of Fire IV → `False`, Breath of Warfare →
`True`, Half-Life 2 → `True`, absent game → `null`, punctuation-insensitive lookup still applies.

Parsed 14,336 where the raw grep said 14,344. **Not a parsing miss** — `Parse` drops 58 keys that
differ only in case, by design (52,973 − 58 = 52,915 = `GameCount`). Confirmed before shipping;
noted in `Gotchas.md` because it will look like a bug to the next person who counts.

`run-linux-tests.sh`, same clone, baseline first:

```
pristine main : 63 passed, 0 failed
with change   : 69 passed, 0 failed   (identical failure sets)
```

Three fixture games differ in **nothing the scanner can observe except their cloud block** — with a
single installed game the suite cannot distinguish reading the field from returning a constant, and
a constant is the bug. Against pre-fix code:

```
FAIL: no cloud block -> not flagged as Steam Cloud
PASS: cloud: steam: true -> flagged as Steam Cloud
FAIL: cloud: gog only -> not flagged as Steam Cloud
FAIL: --no-cloud keeps a Steam game the manifest does not cloud
FAIL: --no-cloud keeps the gog-cloud-only game
PASS: --no-cloud drops the genuinely cloud-synced game
```

The two passes are intentional: they pin the opposite direction, so a lazy "always false" fix cannot
pass the suite either.

## Found in passing, unrelated

**`run-winagent-tests.ps1` reads 113/114 on pristine `main`.** `WA-01 the dashboard is told the real
reason` fails at `ff4464b` with no local changes — verified by building and running a detached
worktree, so it is not the `.verify/` trap. The command executes and returns a result; the result
text no longer matches `running`. The 114 baseline in `CONTEXT.md` has drifted. Not investigated.

## Not covered

The agent-ui chips and the Deck Game Mode filter row need no change — they read `HasSteamCloud` off
the DTO — but neither has a harness, so neither surface is tested. Windows has no Cloud-specific
check either; the shared logic it depends on is covered above.
