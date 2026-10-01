# Playnite add-on database submission

Not started on our side; on hold upstream. Split out of `tasks/playnite-plugin/plan.md` (Phase 16) on 2026-10-01, when
the plugin task was closed as done.

## Where it stands

- Submitted 2026-09-17 as [JosefNemec/PlayniteAddonDatabase#661](https://github.com/JosefNemec/PlayniteAddonDatabase/pull/661)
  (`addons/generic/Marwanello_SaveLocker.yaml`). Still open, not accepted.
- The maintainer's reply (2026-09-30): new plugin submissions are on hold because of the volume of new submissions and
  plugin-security concerns. A new add-on database with its own submission and verification system is planned for
  Playnite 11, to be backported to Playnite 10, and plugin submissions resume then. Themes are still accepted; plugins
  from long-standing trusted community members may be.
- Nothing in the submission was rejected; there is nothing to do until the new system opens.

## Fix before resubmitting

1. **`installer.yaml` still lists v0.1.1.** That release was packed before the version-stamping fix
   (SaveLocker-Playnite PR #7), so its `.pext` reports itself as 0.1.0. Point the entry at v0.1.2 (or the newest
   release) and check that the installed plugin reports the version the manifest advertises.
2. **The listing links the upstream repo.** Its `Description` and `Links` name `https://github.com/SkorcherX/SaveLocker`,
   and the PR body says the plugin needs that agent. The plugin needs the agent from `Marwanello/SaveLocker` (the plugin
   README's fork-compatibility note) until upstream has the Playnite routes. Use whichever is right at resubmission time.
3. Re-read the new database's rules when they are published; the manifest was written against the current README
   (SaveLocker-Playnite `docs/addon-submission/`, `docs/logs/2026-09-16_group-6-phase-16-prep.md`).

## Until then

The plugin installs without the database: the agent UI's "Playnite plugin" card installs it in one click (Phase 19),
and the `.pext` is on the plugin repo's Releases page.

## Done when

The plugin is listed in Playnite's add-on browser, and its version there matches the newest release.
