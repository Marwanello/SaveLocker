import { useState, useEffect, useMemo } from 'react';
import { HelpMarkdown } from './HelpMarkdown';
import { articles, categories } from '../help/index';
import { Page } from './ui/Page';
import { PageHead } from './ui/PageHead';
import { Card } from './ui/Card';
import { SearchField } from './ui/SearchField';

function getSlugFromHash(): string | null {
  const m = location.hash.match(/^#help\/(.+)$/);
  return m ? m[1] : null;
}

/** plan.md Phase 12.2: the prototype's `docs` grid — the article list beside the article, on the kit. */
export function HelpView() {
  const [selectedSlug, setSelectedSlug] = useState<string>(getSlugFromHash() ?? articles[0].slug);
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
    return articles.filter(a => a.title.toLowerCase().includes(q) || a.content.toLowerCase().includes(q));
  }, [query]);

  const current = articles.find(a => a.slug === selectedSlug) ?? articles[0];

  return (
    <Page>
      <PageHead
        title="Help"
        sub={`${articles.length} articles · ships with the console, works offline`}
        actions={<SearchField value={query} onChange={setQuery} placeholder="Search articles" aria-label="Search help articles" />}
      />
      <div className="grid gap-3 md:grid-cols-[240px_minmax(0,1fr)] items-start">
        <Card flush className="md:sticky md:top-0">
          <nav aria-label="Help articles" className="py-1.5">
            {query.trim()
              ? <>
                  <Heading>Results ({filtered.length})</Heading>
                  {filtered.map(a => <Item key={a.slug} title={a.title} active={a.slug === current.slug} onClick={() => selectArticle(a.slug)} />)}
                  {filtered.length === 0 && <p className="px-4 py-3 text-xs text-dim">No articles match.</p>}
                </>
              : categories.map(cat => (
                  <div key={cat}>
                    <Heading>{cat}</Heading>
                    {articles.filter(a => a.category === cat).map(a =>
                      <Item key={a.slug} title={a.title} active={a.slug === current.slug} onClick={() => selectArticle(a.slug)} />)}
                  </div>
                ))}
          </nav>
        </Card>
        <Card>
          <article className="help-content max-w-[68ch]">
            <HelpMarkdown>{current.content}</HelpMarkdown>
          </article>
        </Card>
      </div>
    </Page>
  );
}

function Heading({ children }: { children: React.ReactNode }) {
  return <div className="px-4 pt-3 pb-1.5 text-[10px] tracking-[0.12em] uppercase text-faint">{children}</div>;
}

function Item({ title, active, onClick }: { title: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? 'page' : undefined}
      className={`block w-[calc(100%-12px)] mx-1.5 text-left px-2.5 py-[7px] rounded-lg text-[12.5px] border-0 cursor-pointer
        focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent
        ${active ? 'bg-accent-soft text-accent-ink font-semibold' : 'bg-transparent text-dim hover:bg-hover hover:text-fg'}`}
    >
      {title}
    </button>
  );
}
