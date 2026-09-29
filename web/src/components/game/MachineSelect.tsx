import type { AgentHealth, Machine } from '../../types';
import { ago } from '../../format';
import { osForMachine, osLine } from '../../machineOs';
import { OsBadge } from '../ui/OsLogo';
import { Select } from '../ui/Select';

interface Props {
  machines: Machine[];
  health: AgentHealth[];
  value: string | null;
  onChange: (machineId: string) => void;
  label: string;
  placeholder?: string;
  variant?: 'pill' | 'field';
  align?: 'start' | 'end';
}

/** A machine picker: each machine with its OS logo, and under its name the OS and whether it is online. */
export function MachineSelect({ machines, health, value, onChange, label, placeholder, variant, align }: Props) {
  const options = machines.map(m => {
    const os = osForMachine(health, m.id);
    const h = health.find(x => x.machineId === m.id);
    const state = !h?.lastHeartbeat ? 'never reported' : h.online ? 'online' : `seen ${ago(h.lastHeartbeat)}`;
    return {
      value: m.id,
      label: m.name,
      sub: [osLine(os), state].filter(Boolean).join(' · '),
      icon: <OsBadge os={os} size="sm" />,
    };
  });
  return (
    <Select value={value} options={options} onChange={onChange} label={label}
      placeholder={placeholder} variant={variant} align={align} />
  );
}
