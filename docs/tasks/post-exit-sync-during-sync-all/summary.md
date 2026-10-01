# `post-exit-sync` while Sync all runs

Not started. Moved out of `Backlog.md` on 2026-10-01; the notes below are as they stood there.

`POST /api/games/{id}/post-exit-sync` answers 409 whenever the agent's global sync gate is held (Sync all, or another launch-gate call), on the premise that the running sync converges. It converges the push but never `OnGameExitAsync`'s lease release and renewer stop, so a Playnite/Decky game that exits mid Sync all keeps its lease renewed on this machine (locking the fleet out) unless the caller retries. Found in the PR #46 review; pre-existing → `tasks/checkpoint-ui/implementation-grouping.md` → Group 4 review fixes.
