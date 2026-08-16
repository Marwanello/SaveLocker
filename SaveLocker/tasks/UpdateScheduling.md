# Task — Real scheduling for the agent-update auto-fetch

**Created:** 2026-08-16

**Target:** `web/src/components/ConfigView.tsx` (`autoFetchHours` UI, `~line 27` and `~line 306-347`),
`src/Server/Services/Settings.cs`/`SettingsService.cs`, `src/Server/Services/AgentInstallerPollerService.cs`.

**Goal:** today the auto-fetch schedule is a single "every N hours" number (`settings.autoFetchHours`,
edited inline on the card via a text input + Save button). Replace with:

1. **Card shows the schedule as a plain sentence** ("every 6 hours", "weekly on Sunday at 3:00 AM",
   whatever the current mode is) with an **Edit** button, not an inline editable field.
2. **Edit opens a schedule picker** supporting: the existing hourly-frequency mode (kept, since it's
   simple and works), plus weekly (day of week + time of day) and monthly (day of month + time of
   day) modes.

**Motivation (maintainer, 2026-08-16):** wants scheduling to feel like a real schedule, not just a
raw interval, while keeping the option to fall back to "just every N hours".

---

## Before starting

This is the largest of the four Agent-Updates-adjacent tasks and has real design decisions — do this
**after** `AgentUpdatesRedesign.md` if both are queued, since the schedule display is part of that
card's redesigned read-only summary and building it twice is wasted work.

Read `AgentInstallerPollerService.cs` fully — it currently just polls every `autoFetchHours`. Moving
to weekly/monthly needs either: a persisted "next scheduled run" timestamp the poller checks against
each tick (simplest, survives restarts correctly), or a full cron-like scheduler (unnecessary
complexity for one job). Prefer the persisted next-run-timestamp approach.

## Decisions to make first

1. **Storage shape** for the new setting — a single `AutoFetchSchedule` value (JSON: mode +
   day-of-week/day-of-month + time-of-day + legacy hours) replacing `autoFetchHours`, or additive
   fields alongside it? Check `SettingsService.cs` and the `Settings` DTO in `Contracts.cs` for how
   settings are currently read/written before choosing — prefer whatever requires the least migration
   churn.
2. **Timezone.** "Time of day" needs a timezone reference — server's local time, UTC, or the browser's
   (which may differ from the server's, e.g. a Docker container on unRAID vs. the admin's laptop).
   Server-local or UTC is simplest and avoids storing a timezone per schedule; decide and be explicit
   about it in the UI copy ("3:00 AM server time" or similar) so it isn't ambiguous.
3. Does changing the schedule take effect immediately (recompute next-run now) or only from the next
   natural tick? Immediate recompute is the less surprising choice.

## Verification

- Server-side: a unit/integration check that a weekly schedule computes the correct next-run
  timestamp across a few cases (including "today is the scheduled day but the time already passed"
  → next week, not today).
- Manual: set each of the three modes in the UI, confirm the card's summary sentence matches, confirm
  `autoFetchHours`-only mode still works exactly as it does today (no regression for anyone who
  hasn't touched the new UI).
- Regenerate `openapi.json` / `api-types.ts` if the settings contract shape changes.

**Stop and report after this task.**
