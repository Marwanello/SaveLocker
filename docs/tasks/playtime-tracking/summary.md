# Playtime and "last played" — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). No plan
file yet. **Priority: Low** — nice to have, mostly built on what the agent already knows.

---

## The idea

Show when each game was last played, on which machine, and roughly for how long — in the console game page and
the agent's game list. The agent already knows when a game starts and stops: `GameActivity` ("is this game
running?"), the Windows `ProcessWatcher`, the Linux launch wrapper (`savelocker run`), and the Playnite
plugin's start/exit hooks. The lease (`Lease`) already records which machine has a game checked out.

## What competitors do

- **Hoard** (AGPL — ideas only) `crates/hoard-agent/src/playtime.rs`: each process poll adds the elapsed time
  to every still-running game, bucketed by local day; the step is capped so a suspended laptop does not count
  hours of sleep. Local only. `presence.rs` heartbeats "playing X" so other devices show it live.
- **OpenSave** (MIT) `internal/sessions/sessions.go`: a play session is a process start and end. On Linux it
  identifies the game from `SteamAppId` in the process environment, or `AppId=` on Steam's launcher command
  line; otherwise by exe path or by the process running from the install folder. One missed poll is not the
  end of a session (games restart themselves).

## Proposed shape

1. Agent: record a session (start, end, machine) when an existing start/stop signal fires; cap a session at
   the time the machine went to sleep or the agent last saw the process.
2. Report sessions on the heartbeat; server stores `PlaySession` rows; console shows *Last played* and a total
   on the game page and as a sort option on the Games tab.
3. Optional: "Playing now on Steam Deck" from the lease, which the console has today but does not phrase that
   way.

## Touch points

`src/Agent.Core/GameActivity.cs`, `Watchers.cs`, `HealthReporter.cs`, `src/Agent.Linux/ProtonRun.cs`, server
`Entities.cs` + migration, `HealthService.cs`, `web/` game page and Games sort, `openapi.json` + both
`api-types.ts`.
