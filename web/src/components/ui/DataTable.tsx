import { Fragment, type ReactNode } from 'react';

/** `k` the row's key (fg, semibold) · `m` mono (ids, paths) · `n` a number · `wrap` long text that may wrap. */
export type CellKind = 'k' | 'm' | 'n' | 'wrap';

export interface Column<T> {
  head: ReactNode;
  cell: (row: T) => ReactNode;
  kind?: CellKind | CellKind[];
  /** Right-aligns the column (actions, sizes). */
  end?: boolean;
  /** Screen-reader name for a column whose visible head is empty (an actions column). */
  label?: string;
}

interface Props<T> {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  /** Rendered in place of the body when `rows` is empty — usually an `EmptyState`. */
  empty?: ReactNode;
  caption?: string;
  /** A row's expanded part, drawn as a full-width row beneath it; null or undefined while it is closed. */
  expanded?: (row: T) => ReactNode;
}

/** The prototype's `table.t` (`.dt` in index.css): uppercase heads, row rules, row hover, and the cell
 *  kinds above. A wrapper scrolls sideways rather than letting a wide table push past its card. */
export function DataTable<T>({ columns, rows, rowKey, empty, caption, expanded }: Props<T>) {
  if (rows.length === 0 && empty) return <>{empty}</>;
  return (
    // `relative` keeps an sr-only column label inside the scroller: positioned against the page, it
    // widened the whole page on a phone. A container only when rows expand: the expanded part is sized
    // to the visible width (`cqw`), not the table's, so a table wider than its card scrolls sideways
    // without taking the tree with it.
    <div className={expanded ? 'relative overflow-x-auto @container' : 'relative overflow-x-auto'}>
      <table className="dt">
        {caption && <caption className="sr-only">{caption}</caption>}
        <thead>
          <tr>
            {columns.map((c, i) => (
              <th key={i} className={c.end ? 'text-right' : undefined}>
                {c.head}{c.label && <span className="sr-only">{c.label}</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(r => {
            const more = expanded?.(r);
            return (
              <Fragment key={rowKey(r)}>
                <tr className={more ? 'open' : undefined}>
                  {columns.map((c, i) => {
                    const kinds = c.kind === undefined ? [] : Array.isArray(c.kind) ? c.kind : [c.kind];
                    return (
                      <td key={i} className={[...kinds, c.end ? 'text-right' : ''].join(' ').trim() || undefined}>
                        {c.cell(r)}
                      </td>
                    );
                  })}
                </tr>
                {more && (
                  <tr className="more">
                    <td colSpan={columns.length}>{more}</td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
