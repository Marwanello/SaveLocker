# Save-path autodetection harness

Answers *"can users trust the path we set?"* with a number instead of an anecdote.

It materialises dummy save trees at the paths the **real** Ludusavi manifest claims, then scores the
production `PathResolver` / `ManifestLoader` against them. No Steam, no Proton, no GPU and no Steam
Deck are involved — the resolver only reads a token map and the filesystem, and `Oracle` fakes both.
It runs fine under WSL.

```bash
tests/detection/run-detection-tests.sh
```

> Run it from the WSL **ext4** home, never `/mnt/c`. DrvFs is case-insensitive, and a
> case-sensitivity miss is one of the Deck-only failures this harness exists to catch — a green run
> on DrvFs would be a fiction. The script refuses to run there.

## The three modes

| Mode | What it does | Gates CI? |
|------|--------------|-----------|
| `coverage` | Pure analysis over the whole manifest: which tokens appear in real *save* paths, and how many games each token set can resolve. | no |
| `sweep` | Builds fixture trees for a seeded random sample and scores the resolver. Measures the hit rate. | no |
| `pinned` | Same, for named games in `pinned-cases.tsv` whose specific quirks must never regress. | **yes** |

`sweep` reports; `pinned` gates. A hit rate that drifts by a percent is not a build failure, but a
named game that changes outcome is.

## Why there is a separate `Oracle`

`Oracle` knows **every** manifest placeholder, including the ones production cannot expand. That is
the whole point: it builds the fixtures, and `PathResolver` is scored on how many it can find. If
fixtures were built with the production resolver, every test would pass by construction and the gap
we are trying to close would be invisible.

The Windows-token half of `Oracle` is a deliberate mirror of `PathResolver.Proton`. **If the two
disagree the harness reports false misses**, so a change to either must be made in both.

## Outcomes

- `HIT` — returned the directory the manifest designates as a save.
- `MISS(<token>)` — returned nothing, and the named unsupported token is why.
- `WRONG(config folder)` — returned a directory the manifest tags **config**, not save. Worse than a
  miss: the UI presents it as a confident answer.
- `WRONG(unrelated folder)` — returned something that is neither.
- `SKIP` — no save path that can be expanded even by the oracle (relative or store-rooted).

## Baseline (seed 1, 400 games)

Before `<base>` / `<root>` / `<storeUserId>` and tag filtering landed — i.e. as of v0.5.1:

```
     229  HIT          hit rate 57.5%
     154  MISS         119 <base>, 18 <storeUserId>, 10 <root>+<storeUserId>, 7 <base>+<storeUserId>
      15  WRONG        almost all "config folder" — tags were not filtered
       2  SKIP
```

After:

```
     394  HIT          hit rate 99.5%
       2  MISS         <storeUserId> embedded mid-segment; unknowable, correctly refused
       0  WRONG
       4  SKIP         malformed manifest entries the fixture builder will not create
```

Identical on Windows and on ext4 (WSL, case-sensitive).

Nothing left in that table is a resolver fault. Two entries are malformed — one hardcodes
`C:/Users/Public/Documents/...`, another is `/AppData/LocalLow/...` with its `<home>` missing — and
two put `<storeUserId>` inside a path segment rather than as a whole one, where the id cannot be
known. All four are refused rather than guessed, so the user is asked to pick a folder instead of
being handed a confident path that is wrong.

Analytic coverage over all 20,479 manifest entries that have a Windows save path:

```
   12,661  resolvable before
   +5,932  unlocked by <base>
   +1,886  unlocked by <root> + <storeUserId>
        0  still unresolvable
```

## Note on the Windows agent

The sweep runs in **Proton** flavour because `PathResolver.Proton` takes its root as a parameter and
is therefore injectable. `PathResolver.Windows()` reads real known folders via
`Environment.GetFolderPath`, which cannot be redirected, so it cannot be fixture-driven in-process.

This costs less than it looks: the manifest data, the template-expansion logic and the wildcard
trimming are shared, and only the token *map* differs. `coverage` is platform-neutral and covers
both. A Windows-specific regression in the map itself still needs `tests/run-winagent-tests.ps1`.
