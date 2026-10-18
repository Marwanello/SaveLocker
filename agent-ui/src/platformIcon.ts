import { CONSOLE_ICONS, EMULATOR_ICONS, SYSTEM_ICONS } from './platformIcons.generated'

// Which icon (tasks/emulator-saves Phase 21) an emulator, a console folder or a release's console takes; PlatformIcon draws it.

/** The icon id for an emulator ("RetroArch") or a console folder name ("snes"), or null when there is none. */
export function emulatorIcon(name: string | null | undefined): string | null {
  return name ? EMULATOR_ICONS[name] ?? null : null
}
export function systemIcon(system: string | null | undefined): string | null {
  return system ? SYSTEM_ICONS[system.toLowerCase()] ?? null : null
}
/** The icon id for a release's console name ("PlayStation 2", "Nintendo Switch"), or null. */
export function consoleIcon(name: string | null | undefined): string | null {
  return name ? CONSOLE_ICONS[name] ?? null : null
}
