# Release Notes — Pending

Accumulating draft for the **next release**. Nothing is pending right now.

Everything that was drafted here shipped in **v0.5.0** (2026-07-29) and now lives in
`web/src/releases/0.5.0.md` — the console's What's New page *and* the GitHub Release body, so it is
written once and cannot drift.

---

## How to use this file

Draft each user-facing bullet here **as the fix lands**, not reconstructed from `git log` at tag
time. Match the voice of the released notes: plain language, what the user noticed, no finding IDs.

At tag time, lift the drafts into `web/src/releases/<version>.md` and add the entry to
`web/src/releases/index.ts`. Then check the rendered page, not just the build — the notes are
rendered **without raw HTML**, so `<br>` and `<angle-bracketed>` placeholders that are fine in this
vault come out as literal text or vanish entirely. That has happened once already.

Keep a **"not yet verified"** section under each draft, and honour it at tag time. The rule that
matters: if a fix is implemented but its user-visible guarantee has not been demonstrated, **reword
the bullet to describe the change rather than assert the outcome** — do not quietly drop it, and do
not publish the stronger claim. v0.5.0's Windows credentials bullet is the worked example, and its
Known Issues entry is the accompanying honesty.

---

## Carried forward from v0.5.0

Not release notes, but the reason the next release may need to *amend* v0.5.0's:

- The **second-Windows-account ACL test** has still not been run. When it passes, reword the
  credentials bullet in `web/src/releases/0.5.0.md` from the permission change to the guarantee, and
  drop the matching Known Issues entry. Editing that file updates the console page and the GitHub
  Release together.
- The **Linux suite and Deck verification** are outstanding for fixes that already shipped. If either
  turns up a problem, it is a v0.5.1 note rather than an amendment. See [[Backlog]].
