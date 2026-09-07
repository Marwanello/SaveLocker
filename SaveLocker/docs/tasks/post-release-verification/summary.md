# v0.5.0 post-release verification — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim (only `logs/` links repointed at `../logs/`), kept as the starting point for a future `plan.md`.

---

**All three bug bounties shipped in v0.5.0 (2026-07-29).** Code is on `main`; what remains is the verification that did not happen before the tag. Write-ups: `../logs/2026-07-29_winagent-bugbounty.md`, `../logs/2026-07-29_linuxagent-bugbounty.md`, `../logs/2026-07-27_console-bugbounty.md`.

- **v0.5.0 post-release verification.** Ordered by what carries the most risk of the release notes being wrong:
  - **Deck verification** — the five scenarios in `../logs/2026-07-29_linuxagent-bugbounty.md` → Verification. Hardware available since 2026-07-19. Fold in the two v0.5.1 Deck fixes while there (one ring on open with A working; a resting cursor must not paint a second selector) and a real save-path detection check now that `<base>` resolves from `StartDir`.
  - **Second-Windows-account ACL test (WA-03).** The one with a user-visible consequence: the credentials are ACL-locked to the enrolling account and asserted against the well-known SIDs, but no second account has ever tried to read them, and it is unconfirmed that the enrolled user can still sync *and take a silent update* after a reboot. **v0.5.0's notes describe the change rather than promising the guarantee, and Known Issues says so — reword `web/src/releases/0.5.0.md` once this passes.** That file is both the console page and the GitHub Release body, so one edit fixes both.
  - Remaining Windows gates: fresh-VM install, a real game, a real non-Steam Steam shortcut, and the first-run Settings deep link on a cold WebView2 profile (the automated test drives the same code path through a refused launch, because the prompt is a modal dialog no test can answer).
  - **LAN enrollment-URL check** on the real deployment (`../logs/2026-07-27_console-bugbounty.md` → Verification).
