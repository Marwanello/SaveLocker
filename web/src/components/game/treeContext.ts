import { createContext, useContext } from 'react';

/** The open tree's name on a title's page ("RetroArch"), or null for a game shown alone: confirm texts name it
 *  ("Delete this RetroArch version?"), since every action on the page acts on that one tree (tasks/multi-emulator-games). */
export const TreeContext = createContext<string | null>(null);

/** " RetroArch" inside a title, "" otherwise — for "Delete this{tree} version". */
export function useTreeWord(): string {
  const tree = useContext(TreeContext);
  return tree ? ` ${tree}` : '';
}
