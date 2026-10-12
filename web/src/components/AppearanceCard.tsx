import { useState } from 'react';
import { api } from '../api';
import { ACCENTS, MARKS, THEMES, isLight, saveLook, setLook, useLook } from '../appearance';
import type { AccentId, Look, Theme } from '../appearance';
import type { AgentHealth, Settings } from '../types';
import { Card } from './ui/Card';
import { Chip } from './ui/Chip';
import { Mark } from './ui/Mark';
import { Seg } from './ui/Seg';
import { Switch } from './ui/Switch';

interface Props {
  settings: Settings;
  /** Each machine's last heartbeat — it says whether that machine follows the console's look. */
  health: AgentHealth[];
  /** Re-read settings after a save, so the pushed/not-pushed state shown here is the server's. */
  onSaved: () => void;
}

/**
 * Theme, accent and app icon — stored on the server and, when "Push to agents" is on, handed to every
 * enrolled machine on its next heartbeat (docs/tasks/checkpoint-ui/plan.md, Phase 4). A change is applied
 * to this page first and saved after: it is a preference, and the click should feel instant. If the save
 * fails the page goes back to what it was and says why.
 */
export function AppearanceCard({ settings, health, onSaved }: Props) {
  const look = useLook();
  const pushing = settings.appearance?.pushToAgents ?? true;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function save(change: Partial<Look>, push: boolean = pushing) {
    const previous = look;
    const next = { ...look, ...change };
    setError('');
    setBusy(true);
    setLook(next);
    try {
      const saved = await saveLook(() => api.setAppearance({ ...next, pushToAgents: push }));
      setLook(saved.look);
      onSaved();
    } catch (e) {
      setLook(previous);
      setError(e instanceof Error ? e.message : 'Could not save the appearance.');
    } finally {
      setBusy(false);
    }
  }

  // An agent that predates the heartbeat field reports neither, so it is in neither list.
  const following = health.filter(h => h.followsConsoleAppearance === true);
  const ownLook = health.filter(h => h.followsConsoleAppearance === false);
  const names = (hs: AgentHealth[]) => hs.map(h => h.online ? h.machineName : `${h.machineName} (offline)`).join(', ');

  const light = isLight(look.theme);
  const mark = MARKS.find(m => m.id === look.mark);

  return (
    <Card title="Appearance" headerRight={<Chip>{mark?.name}</Chip>}>
      <div className="grid grid-cols-[88px_1fr] items-center gap-x-4 gap-y-[18px]">
        <div className="text-xs text-dim">Theme</div>
        <div>
          <Seg<Theme>
            aria-label="Theme"
            value={look.theme}
            options={THEMES.map(t => ({ value: t.id, label: t.name }))}
            disabled={busy}
            onChange={v => void save({ theme: v })}
          />
        </div>

        <div className="text-xs text-dim">Accent</div>
        <div className="flex items-center gap-2 flex-wrap">
          {(Object.keys(ACCENTS) as AccentId[]).map(id => (
            <button
              key={id}
              type="button"
              title={ACCENTS[id].name}
              aria-label={ACCENTS[id].name}
              aria-pressed={look.accent === id}
              disabled={busy}
              onClick={() => void save({ accent: id })}
              style={{ background: light ? ACCENTS[id].light : ACCENTS[id].dark }}
              className={`w-[34px] h-[34px] rounded-[10px] border-2 p-0 cursor-pointer shadow-[inset_0_0_0_1px_rgba(0,0,0,.18)]
                transition-transform duration-150 ease-[var(--ease)] hover:scale-[1.08] disabled:cursor-default
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
                ${look.accent === id ? 'border-fg' : 'border-transparent'}`}
            />
          ))}
          <span className="text-[11px] text-dim ml-1">{ACCENTS[look.accent].name}</span>
        </div>

        <div className="text-xs text-dim">App icon</div>
        <div className="flex items-center gap-2 flex-wrap">
          {MARKS.map(m => (
            <button
              key={m.id}
              type="button"
              title={m.name}
              aria-label={m.name}
              aria-pressed={look.mark === m.id}
              disabled={busy}
              onClick={() => void save({ mark: m.id })}
              className={`w-[34px] h-[34px] rounded-[10px] border-2 bg-tile grid place-items-center p-0 cursor-pointer
                transition-transform duration-150 ease-[var(--ease)] hover:scale-[1.08] disabled:cursor-default
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
                ${look.mark === m.id ? 'border-fg' : 'border-transparent'}`}
            >
              <Mark id={m.id} size={20} title={m.name} />
            </button>
          ))}
        </div>
      </div>

      <div className="mt-[18px] pt-[14px] border-t border-row flex items-center justify-between gap-4">
        <div className="text-[13px] text-fg">
          Push appearance to agents
          <small className="block text-[11.5px] text-dim mt-[3px] max-w-[46ch]">
            Every enrolled machine picks up this theme, accent and icon on its next heartbeat, about 20 seconds.
            A machine can opt out from its own Settings.
          </small>
        </div>
        <Switch
          aria-label="Push appearance to agents"
          checked={pushing}
          disabled={busy}
          onChange={v => void save({}, v)}
        />
      </div>

      <div className="mt-[10px] flex items-center gap-2 flex-wrap">
        {!pushing
          ? <Chip tone="warn">Not shared</Chip>
          : following.length > 0
            ? <Chip tone="ok">{following.length} machine{following.length === 1 ? '' : 's'} following</Chip>
            : <Chip>No machine following</Chip>}
        <span className="font-mono text-[10.5px] text-dim">
          {!pushing
            ? 'each machine keeps its own'
            : [following.length > 0 && names(following), ownLook.length > 0 && `own look: ${names(ownLook)}`]
                .filter(Boolean).join(' · ')}
        </span>
      </div>

      {error && <div role="alert" className="mt-3 text-xs text-accent-ink">{error}</div>}
    </Card>
  );
}
