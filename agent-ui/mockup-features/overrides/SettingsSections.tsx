import { useEffect, useState, type ReactNode } from 'react'

/** The Settings page's sections, most used first (planned, 2026-10-25). */
export const SECTIONS = [
  { id: 'sync', label: 'Sync', sub: 'When saves are backed up and what this device keeps' },
  { id: 'games', label: 'Games', sub: 'What this device syncs, and how launches are caught' },
  { id: 'server', label: 'Server', sub: 'The server this device talks to, and who uses it' },
  { id: 'updates', label: 'Updates', sub: 'The agent’s own version' },
  { id: 'look', label: 'Look & controls', sub: 'Theme, text size and the controller' },
  { id: 'emulators', label: 'Emulators', sub: 'Settings for one emulator' },
] as const

/** A sticky row of section buttons under the page title; the one in view is pressed. */
export function SettingsNav() {
  const [current, setCurrent] = useState<string>(SECTIONS[0].id)
  useEffect(() => {
    const els = SECTIONS.map(s => document.getElementById(`set-${s.id}`)).filter((e): e is HTMLElement => !!e)
    const io = new IntersectionObserver(entries => {
      const top = entries.filter(e => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0]
      if (top) setCurrent(top.target.id.slice(4))
    }, { rootMargin: '-80px 0px -55% 0px' })
    els.forEach(e => io.observe(e))
    return () => io.disconnect()
  }, [])
  return (
    <nav className="slf-setnav" aria-label="Settings sections">
      <div className="sl-seg">
        {SECTIONS.map(s => (
          <button key={s.id} type="button" aria-pressed={current === s.id} data-pad-label={`${s.label} section`}
            onClick={e => {
              setCurrent(s.id)
              // The page is its own scroller (.sl-page); land the section just under this sticky bar.
              const page = e.currentTarget.closest('.sl-page'), sec = document.getElementById(`set-${s.id}`), bar = e.currentTarget.closest('nav')
              if (page && sec && bar) page.scrollTo({ top: page.scrollTop + sec.getBoundingClientRect().top - bar.getBoundingClientRect().bottom - 8, behavior: 'smooth' })
            }}>
            {s.label}
          </button>
        ))}
      </div>
    </nav>
  )
}

export function SettingsSection({ id, children }: { id: (typeof SECTIONS)[number]['id']; children: ReactNode }) {
  const s = SECTIONS.find(x => x.id === id)!
  return (
    <section id={`set-${id}`} className="slf-setsec" aria-labelledby={`set-${id}-h`}>
      <header className="slf-setsec__head">
        <h3 id={`set-${id}-h`}>{s.label}</h3>
        <span>{s.sub}</span>
      </header>
      {children}
    </section>
  )
}
