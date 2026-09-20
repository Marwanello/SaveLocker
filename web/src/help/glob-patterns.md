# Exclude patterns (glob filters)

## What exclude patterns do

Exclude patterns let you tell SaveLocker which files **not** to include in a save archive. Common uses:

- Exclude large log files (`*.log`)
- Exclude screenshot folders (`screenshots/**`)
- Exclude shader caches (`shadercache/**`)

## Syntax

SaveLocker uses gitignore-style glob matching.

| Pattern | What it matches |
|---------|----------------|
| `*.log` | Any `.log` file at **any depth** in the save directory |
| `*.tmp` | Any `.tmp` file at any depth |
| `screenshots/**` | Everything inside a `screenshots` folder at the root of the save dir |
| `cache/*.bin` | `.bin` files directly inside a `cache` folder at root |
| `**/cache/**` | Everything inside any folder named `cache`, at any depth |

**Bare patterns** (no `/`) match at any depth — `*.log` will match `saves/foo.log` and `saves/subdir/bar.log` alike.

**Anchored patterns** (contain `/`) are relative to the save directory root — `screenshots/**` only matches a `screenshots` folder at the top level of the save directory.

## Where to set them

Exclude patterns are per-game. In the dashboard:
1. Open the **Games** view and select a game.
2. Open the **Exclude patterns** section in the game detail panel.
3. Type a pattern and press **Add** (or Enter) — each one becomes a chip you can remove with its ×. Press **Save patterns** when the list is right.

While you edit, the dashboard checks your draft against the game's latest uploaded save and tells you how many of its files the patterns match, *before* you save. A pattern the matcher cannot use — for example one with `..` anywhere but the very start — is refused with the reason, and **Save patterns** stays disabled until you remove it. (Saving such a pattern would otherwise stop that game syncing on every machine.)

A game can have at most 100 patterns, each up to 260 characters.

## Global defaults

A server-wide default exclude list applies to every game. It is shown (read-only) in **Configuration → Default exclude patterns** and, as chips, in each game's editor. It is set on the server itself with `Sync:DefaultExcludeGlobs` in its configuration or environment — it is not editable from the dashboard yet.

## 500 MB upload cap

Regardless of exclude patterns, SaveLocker enforces a **500 MB cap** on archive uploads. If a save archive exceeds 500 MB after applying excludes, the upload is rejected. This usually indicates a misconfigured save path (e.g. pointing at the entire game install directory instead of just the save folder) or a missing exclude for large cache files.

## Example: typical log/cache excludes

```
*.log
*.tmp
shadercache/**
screenshots/**
*.dmp
```
