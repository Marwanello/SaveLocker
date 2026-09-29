import type { ReactNode } from 'react';

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
}

/** The prototype's `table.t` (`.dt` in index.css): uppercase heads, row rules, row hover, and the cell
 *  kinds above. A wrapper scrolls sideways rather than letting a wide table push past its card. */
export function DataTable<T>({ columns, rows, rowKey, empty, caption }: Props<T>) {
  if (rows.length === 0 && empty) return <>{empty}</>;
  return (
    <div className="overflow-x-auto">
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
          {rows.map(r => (
            <tr key={rowKey(r)}>
              {columns.map((c, i) => {
                const kinds = c.kind === undefined ? [] : Array.isArray(c.kind) ? c.kind : [c.kind];
                return (
                  <td key={i} className={[...kinds, c.end ? 'text-right' : ''].join(' ').trim() || undefined}>
                    {c.cell(r)}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
