import type { Version } from '../../types';
import { when } from '../../format';

interface Props {
  /** Each machine's newest version. */
  contributors: Version[];
  headId: string | null;
  onSetLatest: (versionId: string) => void;
}

/** Initial-sync wizard: shown when more than one machine has uploaded. */
export function InitialSyncCard({ contributors, headId, onSetLatest }: Props) {
  return (
    <div style={{ background: 'var(--color-tile)', border: '1px solid var(--color-line)', borderRadius: 8, padding: '10px 12px' }}>
      <b>Initial sync — which machine has your real progress?</b>
      <p style={{ fontSize: 12, color: 'var(--color-dim)', marginTop: 2 }}>Sets that machine's newest save as Latest (what every machine pulls).</p>
      <div style={{ display: 'flex', gap: 8, marginTop: 8, flexWrap: 'wrap' }}>
        {contributors.map(v => (
          <button key={v.id} onClick={() => onSetLatest(v.id)}
            style={{ padding: '5px 12px', background: 'var(--color-accent)', color: 'var(--color-on-accent)', border: 'none', borderRadius: 5, fontSize: 12, cursor: 'pointer' }}
          >
            {v.machineName} ({when(v.createdAt)}){v.id === headId ? ' — current' : ''}
          </button>
        ))}
      </div>
    </div>
  );
}
