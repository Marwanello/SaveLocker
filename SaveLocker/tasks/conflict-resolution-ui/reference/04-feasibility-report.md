# Feasibility report

Verdicts per integration, with the specific APIs relied on, what breaks them, and the fallback.
Every claim below is cited to a primary source (official docs, the API's own source, or a real
shipping plugin's source) where one was found; anything not independently verified is marked
**needs verification** rather than asserted. This report was researched against Decky Loader's and
Playnite's current, public documentation and source as of this writing — neither project version-
locks its docs, so re-verify before implementation if significant time has passed.

## 1. Steam Deck — Decky plugin

**Verdict: possible with caveats.**

| Piece | API relied on | Source/version | What breaks it | Fallback |
|---|---|---|---|---|
| Game-page route patch | `routerHook.addPatch('/library/app/:appId', patch)`, exported from `@decky/api` (moved there from `decky-frontend-lib`'s `ServerAPI.routerHook` in the Decky 3.0 rewrite — same object, different import path) | `@decky/api` current; wiki's own worked example patches this exact route ([wiki.deckbrew.xyz/en/plugin-dev/route-patching](https://wiki.deckbrew.xyz/en/plugin-dev/route-patching)) | Any Steam client UI change to the patched React tree's internal shape — there is no stable contract; the wiki's own authoring guide instructs finding the shape by CEF-debugging and trial and error. A real, citable break-and-fix: the ProtonDB Badges plugin (the wiki's own worked example) needed [PR #49](https://github.com/OMGDuke/protondb-decky/pull/49) ("Update decky-frontend-loader to fix valves breaking changes") after a Steam UI change, and separately added defensive shape-checks in [PR #47](https://github.com/OMGDuke/protondb-decky/pull/47) that bail out (no-op) rather than crash when the expected shape isn't found | **The QAM panel, which does not depend on this API at all.** Built defensively per the PR #47 pattern — check the expected shape before mutating, bail to a no-op (chip simply doesn't render on the game page) rather than throwing. |
| Chip modal (`showModal`/`ConfirmModal`) | Both exported plain functions/components from `@decky/ui`'s `Modal.tsx`; `showModal(modal, parent?, props?)`, `ConfirmModal({ strTitle, strDescription, onOK, onCancel, ... })` — full signatures in [wiki.deckbrew.xyz/en/api-docs/.../Modal.md](https://wiki.deckbrew.xyz/en/api-docs/decky-frontend-lib/deck/components/Modal.md) | `@decky/ui` current | Reachable from both a page-patch context and the QAM by API shape (neither is tied to a call site; `showModal`'s optional `parent?: EventTarget` exists to anchor it) — **this specific parity is a reasoned inference from the type signatures, not a directly documented guarantee; needs verification** by building both call sites and confirming empirically. One documented modal-specific gamepad-focus gotcha exists: [decky-loader issue #685](https://github.com/SteamDeckHomebrew/decky-loader/issues/685) — a modal rendered with zero focusable children becomes stuck (unfocusable, undismissable by gamepad) until Steam UI restarts. | Always render at least one focusable element in the modal, even in a loading state (the direct lesson of #685) — never a modal that can be briefly empty while data loads. |
| Gamepad navigation | `Focusable`'s own props (`onGamepadDirection`, `onButtonDown`/`onButtonUp`, `onActivate`, `onCancel`, `actionDescriptionMap`) and `ConfirmModal`'s own `onOK`/`onCancel`/`onEscKeypress` props — both from the same Modal/Focusable source above | `@decky/ui` current | Nav breaks per-widget when a custom row uses a bare `Focusable` instead of `Field focusable` (this codebase's own prior, hard-won lesson, `Gotchas.md` → Decky) — not a Decky bug, a plugin-authoring mistake this design must not repeat | Build every row/control through this codebase's existing "one helper" discipline (`Gotchas.md`: "Build every such row through one helper — this was fixed twice because two places built them by hand") |
| Python backend ↔ daemon | Plain `aiohttp.ClientSession` HTTP calls from the plugin's Python backend to `127.0.0.1:5178` — no Decky-provided primitive for this; `decky.pyi`'s backend stub confirms no networking helper beyond ordinary Python | Verified against a real, current, shipping example: the Zaparoo Decky plugin's backend talks to a sibling `127.0.0.1:7497` JSON-RPC daemon exactly this way ([zaparoo-decky/main.py](https://github.com/ZaparooProject/zaparoo-decky/blob/main/main.py)), short connect timeouts, `aiohttp.ClientError` caught and re-raised as one domain exception type that surfaces to the frontend as a `call()` rejection | The sibling daemon (SaveLocker's own) is down | Already this codebase's own precedent, unchanged: "SaveLocker agent is not running" — a caught connection error, not a crash, not a retry loop |
| Launch interception | **No official Decky hook exists** (confirmed absence across `@decky/api`'s exports, the wiki, and `decky-loader`'s issue tracker — absence of evidence, not a stated negative from the maintainers). A real but unofficial mechanism exists: `SteamClient.Apps.RegisterForGameActionStart`/`CancelGameAction`, undocumented Valve internals reachable because the plugin frontend shares Steam's JS context — real and shipping today in **MoonDeck** ([registerForGameLaunchIntercept.ts](https://github.com/FrogTheFrog/moondeck/blob/main/src/steam-utils/registerForGameLaunchIntercept.ts)) | MoonDeck, current | Any Valve change to this undocumented internal surface | **The existing Steam launch-options wrapper (`savelocker run -- %command%`) — not Decky-dependent at all.** See `03-platform-ux-flows.md` for why this is the recommended primary mechanism regardless of the `SteamClient` option existing. |

**On launch options specifically** (needed context, since it's the existing precedent this whole
integration is built on): this project's own vault already records that the agent "cannot" write
Steam launch options directly and the plugin does it instead — MoonDeck's source confirms the real
mechanism is the same `SteamClient.Apps.SetAppLaunchOptions(appId, value)` JS call from the
**frontend**, not a Python-backend file edit of `localconfig.vdf`
([setAppLaunchOptions.ts](https://github.com/FrogTheFrog/moondeck/blob/main/src/steam-utils/setAppLaunchOptions.ts)) — i.e. this project's own already-shipping plugin independently converged
on the same mechanism a second real plugin uses, which is a meaningful corroboration.

## 2. Windows — Playnite integration

**Verdict: possible.**

| Piece | API relied on | Source/version | What breaks it | Fallback |
|---|---|---|---|---|
| Launch block | `GenericPlugin.OnGameStarting(OnGameStartingEventArgs args)`, `args.CancelStartup = true` (settable bool) | `Playnite.SDK.Plugins`/`Events`, confirmed against SDK source (`source/PlayniteSDK/Plugins/Plugin.cs`, `Events/ApplicationEvents.cs`) and [api.playnite.link docs](https://api.playnite.link/docs/api/Playnite.SDK.Events.OnGameStartingEventArgs.html); current NuGet `PlayniteSDK` 6.16.0 | A Playnite major-version SDK break — **Playnite 11 is a private, from-scratch rewrite with no public repo/docs as of this research; do not assume this surface survives unchanged into 11.x** | None within this integration if it breaks outright on a new Playnite major version — the tray's existing `ProcessWatcher` exit-push and the manual chooser (no pre-launch gate) are what Windows already has without Playnite at all |
| Startup-cancelled companion | `OnGameStartupCancelled(OnGameStartupCancelledEventArgs args)` | Same source | n/a (fires alongside the above) | n/a |
| Exit trigger | `OnGameStopped(OnGameStoppedEventArgs args)` — `ElapsedSeconds`, `ManuallyStopped` | Same source | **Documented, real reliability gap**: Playnite's own docs on [Tracking Mode](https://api.playnite.link/docs/manual/library/games/gameActions.html) and its [FAQ](https://api.playnite.link/docs/manual/library/games/faq.html) state exit detection can fail for emulator/wrapper-launched games without manually setting Tracking Mode to `Folder`; a real, previously-filed issue documents Playnite failing to detect Cemu/Dolphin/Citra exits ([issue #1322](https://github.com/JosefNemec/Playnite/issues/1322)) | Keep the existing `Watchers.cs`/`ProcessWatcher` exit-push running unconditionally, not replaced by this hook — see `03-platform-ux-flows.md` |
| Target framework | **.NET Framework 4.6.2** | `PlayniteSDK` NuGet 6.16.0's own target; [official plugin docs](https://api.playnite.link/docs/tutorials/extensions/plugins.html) state it explicitly | n/a — this is a fixed constraint, not a risk | The plugin is necessarily a separate assembly from `src/Agent` (net10.0-windows) and must talk to the tray over HTTP (`:5178`), same as Decky's Python backend — not a fallback so much as the architecturally-forced shape, confirmed twice now by two independent SDK constraints |
| UI thread safety | `IPlayniteAPI.MainView.UIDispatcher` (`System.Windows.Threading.Dispatcher`) | Explicitly documented: "Playnite's SDK is not fully thread safe... use `UIDispatcher` from `MainView` API" ([same docs page](https://api.playnite.link/docs/tutorials/extensions/plugins.html)) | Calling WPF UI directly from `OnGameStarting`/`OnGameStopped` without marshalling — documented to crash | `PlayniteApi.MainView.UIDispatcher.Invoke(...)` for any dialog shown from these handlers — direct parallel to this codebase's own `UiDispatcher` fix (`Decisions.md` WA-09) |
| Distribution | `.pext` package via the official, real add-on database, `JosefNemec/PlayniteAddonDatabase` (GitHub, PR-based submission) | Confirmed via that repo's own README | n/a | A materially lower bar than Decky's store (no AI-authorship attestation required) — worth noting as a genuine advantage of this half of the feature |

**Needs verification, carried from the research**: the exact minimum/required SDK API version
field Playnite 10.56 enforces on a plugin manifest (a `RequiredApiVersion`-shaped concept was
referenced in add-on-database context but not pinned to an exact field name/semantics in a source
directly fetched); Playnite 11's plugin surface, entirely unknown and not assumed to carry over.

## 3. Linux desktop and headless

**Verdict: possible**, and lowest-risk of the three — everything relied on is either already
shipping in this codebase or a well-established freedesktop.org standard.

| Piece | API relied on | What breaks it | Fallback |
|---|---|---|---|
| Native modal (Deck Game Mode) | `savelocker ui`'s existing Dear ImGui/SDL stack (`Ui/UiApp.cs`, `Widgets.cs`) | Nothing new — same stack already shipping and documented in `Gotchas.md` | The `agent-ui` web chooser (rung 4) |
| Native modal (Linux desktop) | The existing `agent-ui` React SPA served by the daemon on `:5178` | A browser being unavailable at all (true headless server) | CLI (rung 5) |
| Desktop notification | `org.freedesktop.Notifications.Notify` — a standard, stable freedesktop.org D-Bus interface, not Deck- or distro-specific | No session bus / no notification daemon owning that well-known name (exactly the case rung 1's detection step exists to distinguish from "bus present, nothing listening") | Rung 4 (local web chooser) |
| D-Bus client library | **None exists in this codebase today** — a new dependency (e.g. `Tmds.DBus`) is required | n/a — this is new build/maintenance surface, not a runtime risk once chosen | Explicitly scoped optional/deferred in the phased plan; every other rung works without it |
| Local web chooser | The existing `:5178` loopback API + SSH port-forward — already the documented, working pattern for reaching the Deck's daemon UI (`cli-reference.md`'s own `daemon` command doc) | SSH access itself unavailable | CLI (rung 5), if a local terminal is reachable |
| CLI/TUI | New `savelocker conflicts`/`savelocker resolve` commands — plain `AgentCli.cs` additions, same shape as every existing command | n/a | `doctor` naming the condition even if the two new commands were somehow unreachable |
| Out-of-band notify (webhook) | Server-side, fired on `ConflictFlag` create/escalate — no external API dependency until a specific provider (ntfy, email SMTP, etc.) is chosen | Depends entirely on which provider is picked — deferred, not scoped in this pass | Rungs 1–5, all of which are guaranteed not to depend on this rung |

Nothing in this platform's flow relies on an undocumented or version-fragile API — the entire risk
surface here is "did we remember to keep the fallback chain intact," not "will an external party
break this."

## Summary verdicts

| Integration | Verdict |
|---|---|
| Steam Deck — Decky plugin | Possible with caveats — the route-patch chip is genuinely fragile (Valve-internal, no stable contract) and must never be the only way to reach resolution; the QAM fallback is not optional, it's load-bearing for the whole integration's honesty about invariant 3. |
| Windows — Playnite | Possible — the SDK surface needed is real, documented, and current, with one known reliability caveat (`OnGameStopped` for wrapped launches) that's mitigated by keeping the existing exit-push path, and one real forward-compatibility risk (Playnite 11, unknown surface). |
| Linux desktop/headless | Possible, lowest risk — built entirely on freedesktop.org standards and this codebase's own already-shipping `:5178` API and CLI patterns; the one new dependency (a D-Bus client library) is optional and deferred. |
