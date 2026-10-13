/** One package as the DLC check knows it — enough of ContentPackageDto to compare. */
interface Pack { kind: string; id: string }

/**
 * The packages of `needed` that `here` lacks — ContentPackageDto.Missing in the server's Contracts.cs, kept the same. DLC
 * matches by kind and ID; a title update is there when any title update is installed (each replaces the last, and
 * nothing says which is newer).
 */
export function missingPacks<T extends Pack>(needed: T[], here: Pack[]): T[] {
  const key = (p: Pack) => `${p.kind}/${p.id}`.toLowerCase();
  const have = new Set(here.map(key));
  const anyUpdate = here.some(p => p.kind === 'update');
  return needed.filter(p => !(p.kind === 'update' ? anyUpdate : have.has(key(p))));
}
