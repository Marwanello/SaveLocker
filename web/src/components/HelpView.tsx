import { useState, useEffect, useMemo } from 'react';
import { HelpMarkdown } from './HelpMarkdown';
import { articles, categories } from '../help/index';

function getSlugFromHash(): string | null {
  const m = location.hash.match(/^#help\/(.+)$/);
  return m ? m[1] : null;
}

export function HelpView() {
  const [selectedSlug, setSelectedSlug] = useState<string>(
    getSlugFromHash() ?? articles[0].slug
  );
  const [query, setQuery] = useState('');

  useEffect(() => {
    function onHash() {
      const slug = getSlugFromHash();
      if (slug) setSelectedSlug(slug);
    }
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);

  function selectArticle(slug: string) {
    setSelectedSlug(slug);
    location.hash = `help/${slug}`;
  }

  const filtered = useMemo(() => {
    if (!query.trim()) return articles;
    const q = query.toLowerCase();
    return articles.filter(a =>
      a.title.toLowerCase().includes(q) ||
      a.content.toLowerCase().includes(q)
    );
  }, [query]);

  const current = articles.find(a => a.slug === selectedSlug) ?? articles[0];

  return (
    <div style={{ display: 'flex', flex: 1, minHeight: 0 }}>
      {/* Sidebar */}
      <aside style={{
        width: 240,
        flexShrink: 0,
        background: 'var(--color-panel)',
        borderRight: '1px solid var(--color-line)',
        display: 'flex',
        flexDirection: 'column',
        overflowY: 'auto',
      }}>
        {/* Search */}
        <div style={{ padding: '10px 12px', borderBottom: '1px solid var(--color-line)' }}>
          <input
            type="search"
            placeholder="Search articles…"
            value={query}
            onChange={e => setQuery(e.target.value)}
            style={{
              width: '100%',
              background: 'var(--color-raise)',
              border: '1px solid var(--color-line)',
              borderRadius: 4,
              padding: '5px 9px',
              color: 'var(--color-fg)',
              fontSize: 12,
              fontFamily: 'inherit',
              boxSizing: 'border-box',
            }}
          />
        </div>

        {/* Article list grouped by category */}
        {query.trim()
          ? (
            <div>
              <div style={{ padding: '6px 14px 2px', fontSize: 10, fontWeight: 700, color: 'var(--color-safe-ink)', textTransform: 'uppercase', letterSpacing: '0.12em' }}>
                Results ({filtered.length})
              </div>
              {filtered.map(a => (
                <SidebarItem key={a.slug} title={a.title} active={a.slug === selectedSlug} onClick={() => selectArticle(a.slug)} />
              ))}
              {filtered.length === 0 && (
                <div style={{ padding: '12px 14px', fontSize: 12, color: 'var(--color-dim)' }}>No articles match.</div>
              )}
            </div>
          )
          : categories.map(cat => {
            const catArticles = articles.filter(a => a.category === cat);
            return (
              <div key={cat}>
                <div style={{ padding: '8px 14px 4px', fontSize: 10, fontWeight: 700, color: 'var(--color-safe-ink)', textTransform: 'uppercase', letterSpacing: '0.12em', borderBottom: '1px solid var(--color-line)' }}>
                  {cat}
                </div>
                {catArticles.map(a => (
                  <SidebarItem key={a.slug} title={a.title} active={a.slug === selectedSlug} onClick={() => selectArticle(a.slug)} />
                ))}
              </div>
            );
          })
        }
      </aside>

      {/* Content pane */}
      <main style={{
        flex: 1,
        overflowY: 'auto',
        padding: '28px 40px',
        maxWidth: 780,
      }}>
        <div className="help-content">
          <HelpMarkdown>{current.content}</HelpMarkdown>
        </div>
      </main>
    </div>
  );
}

function SidebarItem({ title, active, onClick }: { title: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      style={{
        display: 'block',
        width: '100%',
        textAlign: 'left',
        padding: '7px 14px 7px 16px',
        background: active ? 'var(--color-raise)' : 'transparent',
        border: 'none',
        borderLeft: active ? '2px solid var(--color-line)' : '2px solid transparent',
        borderBottom: '1px solid var(--color-line)',
        cursor: 'pointer',
        fontSize: 12,
        color: active ? 'var(--color-fg)' : 'var(--color-dim)',
        fontWeight: active ? 600 : 400,
      }}
    >
      {title}
    </button>
  );
}
