# Task — Audit Log CSV export

**Created:** 2026-08-16

**Target:** `web/src/components/AuditView.tsx`

**Goal:** an "Export CSV" button next to the existing "↻ Refresh" button in `AuditView.tsx` (`~line
83`) that downloads the currently loaded `entries` as a CSV file — columns matching the visible
table: Time (raw ISO `e.timestamp`, not the localized `formatTs` display string — a CSV should stay
sortable/parseable), Machine, Game, Action, Detail.

**Motivation (maintainer, 2026-08-16):** wants to pull audit history out for external review/filing.

---

## Notes

- Purely client-side. No new server endpoint needed — `entries` is already the full loaded page
  (`api.audit()`, capped at whatever limit `GetAuditLogAsync` defaults to server-side —
  `src/Server/Services/SyncService.cs:1204`, `limit = 200`). If the maintainer wants *all* history,
  not just the last 200, that's a separate, bigger change (paginating/streaming the audit endpoint) —
  don't scope-creep into that here; ship the "export what's currently loaded" version and note the
  200-row cap in a code comment or the button's title/tooltip.
- CSV values need quoting/escaping (commas, quotes, newlines in `detail` or names) — use a small
  local helper, no new dependency needed for this size.
- Trigger the download via a Blob + temporary `<a download>` click, same as any other client-side
  CSV export — no server round-trip.
- Filename: something like `savelocker-audit-{date}.csv`.

## Verification

- Manual: load the Audit Log page, click Export, open the downloaded file, confirm columns and row
  count match the table, confirm a `detail` value containing a comma or quote round-trips correctly
  (open in a spreadsheet app, not just a text editor — that's what catches quoting bugs).

**Stop and report after this task.**
