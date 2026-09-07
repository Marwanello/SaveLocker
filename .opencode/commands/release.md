---
description: Walk through the SaveLocker release rollout
---

$ARGUMENTS

Follow the rollout in order, confirming version number first. Steps:
1. Tag `vX.Y.Z` — CI builds both agents + Docker image.
2. Upload packages in Config → Agent updates (both Windows and Linux rows; GitHub Release assets are NOT auto-offered).
3. Windows agents prompt in tray; Decks stage + install at next start.
4. Redeploy console: `docker compose pull && docker compose up -d`.
Report each step's result before proceeding to the next. If `global.json` SDK or Dockerfile `sdk`/`aspnet` tags need bumping, bump them together.
