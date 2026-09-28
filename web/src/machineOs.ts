import type { AgentHealth } from './types';
import type { LogoKey } from './components/ui/osLogos';

export interface MachineOs {
  /** Null when nothing is known yet: the badge falls back to a plain monitor. */
  logo: LogoKey | null;
  /** "Ubuntu 24.04.1 LTS", "Windows 11 (build 26200)", or the bare platform from an older agent. */
  label: string | null;
  /** "Steam Deck", "WSL" — hardware or layer worth naming beside the OS. */
  device: string | null;
}

/** os-release `ID` → logo. Ubuntu's flavours all report `ubuntu`, so they share its logo. */
const BY_ID: Record<string, LogoKey> = {
  windows: 'windows',
  bazzite: 'bazzite',
  ubuntu: 'ubuntu',
  linuxmint: 'linuxmint',
  pop: 'popos',
  elementary: 'elementary',
  zorin: 'zorin',
  neon: 'kdeneon',
  debian: 'debian',
  deepin: 'deepin',
  fedora: 'fedora',
  nobara: 'nobaralinux',
  rhel: 'redhat',
  centos: 'centos',
  rocky: 'rockylinux',
  almalinux: 'almalinux',
  arch: 'archlinux',
  manjaro: 'manjaro',
  endeavouros: 'endeavouros',
  cachyos: 'cachyos',
  garuda: 'garudalinux',
  nixos: 'nixos',
  gentoo: 'gentoo',
  alpine: 'alpinelinux',
  void: 'voidlinux',
  solus: 'solus',
  kali: 'kalilinux',
};

type Os = NonNullable<AgentHealth['os']>;

/**
 * An unknown distro gets Tux rather than its `ID_LIKE` parent's logo: ChimeraOS drawn as Arch would
 * read as "this machine runs Arch". The tooltip still names it.
 */
export function logoFor(os: Os): LogoKey {
  const id = os.id.toLowerCase();
  if (id === 'steamos') return os.device?.startsWith('Steam Deck') ? 'steamdeck' : 'steam';
  // Bazzite's images have reported both ID=bazzite and ID=fedora with a bazzite VARIANT_ID.
  if (os.variantId?.startsWith('bazzite')) return 'bazzite';
  if (id.startsWith('opensuse')) return 'opensuse';
  return BY_ID[id] ?? 'linux';
}

export function machineOs(health: AgentHealth | undefined): MachineOs {
  const os = health?.os;
  if (os && os.id !== 'unknown') return { logo: logoFor(os), label: os.name, device: os.device ?? null };
  // An agent from before the OS field still says which of the two agents it is.
  if (health?.platform === 'Windows') return { logo: 'windows', label: 'Windows', device: null };
  if (health?.platform === 'Linux') return { logo: 'linux', label: 'Linux', device: null };
  return { logo: null, label: os?.name ?? null, device: null };
}

export function osForMachine(health: AgentHealth[], machineId: string | null | undefined): MachineOs {
  return machineOs(machineId ? health.find(h => h.machineId === machineId) : undefined);
}

/** "SteamOS Holo · Steam Deck" — the one line a tooltip or a caption shows. */
export function osLine(os: MachineOs): string | null {
  return [os.label, os.device].filter(Boolean).join(' · ') || null;
}
