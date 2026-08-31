import { useEffect, useRef, useState } from 'react'
import { api } from './api'
import type { Conflict, SaveVersion, VersionStats } from './types'

/**
 * Machine name/timestamp/size (and file-count/newest-change) for both sides of a set of open
 * conflicts. A `Conflict` only ever carries version ids, so both are fetched lazily per version and
 * cached here — an archive's stats never change once uploaded, so `requestedRef` stops a caller's own
 * poll from re-fetching what it already has, even though the `conflicts` array it passes in is a
 * fresh reference every time (the same pattern the dashboard's `GameDetail.tsx` uses).
 */
export function useConflictVersions(conflicts: Conflict[]) {
  const [versions, setVersions] = useState<Record<string, SaveVersion>>({})
  const [stats, setStats] = useState<Record<string, VersionStats>>({})
  const requestedRef = useRef<Set<string>>(new Set())

  useEffect(() => {
    for (const c of conflicts) {
      for (const vid of [c.versionAId, c.versionBId]) {
        if (requestedRef.current.has(vid)) continue
        requestedRef.current.add(vid)
        api.version(vid)
          .then(v => setVersions(prev => ({ ...prev, [vid]: v })))
          .catch(() => requestedRef.current.delete(vid)) // best-effort — retry on the next poll
        api.versionStats(vid)
          .then(s => setStats(prev => ({ ...prev, [vid]: s })))
          .catch(() => { /* stats are a bonus line, not load-bearing */ })
      }
    }
  }, [conflicts])

  return { versions, stats }
}
