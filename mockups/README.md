# Mockups

**Every new mockup builds on these.** Don't start a fresh mockup project or a hand-drawn page for the agent or the
console: add a feature (or a variant) to the existing ones, and improve them as you go. They are the real apps with a
fake backend, so a mockup always looks and behaves like what ships.

| Mockup | Where | Run | Build one HTML file |
|---|---|---|---|
| Agent UI | `agent-ui/mockup-features/` | `npm run mockups` in `agent-ui/` | `npm run mockups:build` → `mockups/agent.html` |
| Console | `web/mockup-features/` | `npm run mockups` in `web/` | `npm run mockups:build` → `mockups/console.html` |
| Decky and Playnite | `mockups/handheld.html` | open the file | — (hand-built, one file) |

None of it ships: the apps' builds and `tsconfig`s only take `src/`, nothing in `src/` imports a mockup, and the
built bundles are gitignored.

## How the app mockups work

Each `mockup-features/` folder holds:

| Path | What it is |
|---|---|
| `main.tsx`, `index.html` | Starts the real app with the fake backend and the review strip on top |
| Fake backend | Console: `fetchMock.ts` + `fixtures.ts` here. Agent: reuses `agent-ui/mockup/fetchMock.ts` + `fixtures.ts` (the gamepad redesign mockup) |
| `session.ts` (console) | Signs the console in before `api.ts` reads the session |
| `store.ts` | `FEATURES` (each feature, its variants, notes and "Mock only" cases), `CHOSEN` (the maintainer's picks), and the *All chosen* switch |
| `FeatureFrame.tsx` | The strip at the top: feature, variant, notes, Mock only cases, All chosen |
| `overrides.json` | Real component → its copy in `overrides/`. The Vite config resolves every import of the real file to the copy |
| `overrides/` | Copies of real components with the feature added. The real file is never touched |
| `features/` | New pieces that don't exist in the app yet |
| `copy-override.cjs` | Copies a real component into `overrides/` with its imports rewritten |
| `features.css` / `console.css` | Styles for the new pieces |

## Adding a feature

1. **Store:** add a `FeatureDef` to `FEATURES` in `store.ts`: `id` (prefix with the task, e.g. `pt-`, `vh-`, `st-`),
   `task` (add the task's name to the union if it's new), `name`, where it opens (`route` in the agent, `where` in
   the console), `variants` (A, B, C… each with *what* and *try*), `pick` (my proposal until decided), `always`
   (what's true in every variant) and optional `sims` for Mock only cases.
2. **Override a real component** when the feature lives inside one:
   `node mockup-features/copy-override.cjs src/components/…/Thing.tsx`, add the path to `overrides.json`, then edit
   the copy. If it's already overridden, edit the existing copy.
3. **New pieces** go in `features/`. Read the variant with `useVariant('<id>')`: it returns the variant on show, or
   `null` when the feature is off, so the copy renders exactly like the real component when it's off.
4. **Fake data** goes in the fixtures. Keep it believable (real game names, sizes, times) and shared, so features
   combine.
5. **Once the maintainer picks**, put it in `CHOSEN` and update `pick` to "Chosen: … (date)". *All chosen* then
   shows it with every other pick, and the task's plan gets the pick in its *Design (decided)* section.

## Keeping them current

- **When the real component changes** (a shipped feature, a restyle), refresh its copy:
  `node mockup-features/copy-override.cjs <path> --force`, then put the feature back in. Check with `git diff` on
  the copy that only the feature differs from the real file.
- **When a feature ships**, delete its `FEATURES` entry, its `CHOSEN` line, its `features/` file and its changes in
  the copies. Delete a copy (and its `overrides.json` line) once nothing in it differs from the real file.
- Improve the base itself whenever a mockup needs it (more fixtures, another Mock only case, a better strip). It's
  the base for every later mockup.

## Decky and Playnite

They can't run outside Steam and Playnite, so `handheld.html` rebuilds both by hand: Decky from screenshots and
computed styles read off the Deck's live UI (its Steam debug port over SSH), Playnite from a window capture and the
Harmony theme's `Constants.xaml`. Add to this page rather than starting another, and re-capture from the real UI
when it changes.

## Publishing for review

`playthroughs.html`, `version-history.html`, `storage.html` and `combined.html` are the review landing pages
(`kit.css` styles them). Build both bundles, then publish a landing page with `agent.html`, `console.html`,
`handheld.html` and `kit.css` next to it. Deep links: `agent.html#<feature-id>`, `console.html#<feature-id>`,
`#combined` for All chosen, `handheld.html#decky-B` / `#playnite-A`.
