import type { Game } from '../../types';
import { artSrc, artSrcSet } from '../../art';

interface Props {
  game: Game;
  /** `s` — the 38 px square in a row (the icon, since box art would have to be cropped);
   *  `m` — 3:4 box art filling a grid tile; `l` — the 72×96 cover on the game's page. */
  size: 's' | 'm' | 'l';
}

const initials = (name: string) =>
  name.replace(/[^A-Za-z0-9 ]/g, '').split(' ').filter(Boolean).slice(0, 2).map(w => w[0]).join('').toUpperCase() || '?';

const BOX: Record<Props['size'], string> = {
  s: 'w-[38px] h-[38px] rounded-[9px] text-[11px]',
  m: 'w-full aspect-[3/4] rounded-[9px] text-[26px]',
  l: 'w-[72px] h-[96px] rounded-[11px] text-xl',
};

/**
 * Real SteamGridDB art where there is some, the game's initials on a tile where there is none — a game
 * with no match must still be findable. The tile is built from the tokens like agent-ui's GameArt
 * fallback, not the prototype's per-game hues: a view takes every colour from the palette
 * (run-appearance-consistency-tests), and an invented hue would read differently in each theme.
 */
export function GameCover({ game, size }: Props) {
  const url = size === 's' ? (game.iconUrl || game.gridUrl) : game.gridUrl;
  if (url) {
    const [w, set, sizes]: [number, number[], string] =
      size === 's' ? [64, [64, 128], '38px'] : size === 'l' ? [192, [96, 192, 256], '72px'] : [256, [192, 256, 384], '200px'];
    return (
      <img
        src={artSrc(url, w)} srcSet={artSrcSet(url, set)} sizes={sizes}
        alt="" loading="lazy" decoding="async"
        className={`${BOX[size]} object-cover border border-line block shrink-0`}
      />
    );
  }
  return (
    <span
      aria-hidden
      className={`${BOX[size]} grid place-items-center shrink-0 border border-line text-dim font-semibold tracking-[.04em]
        bg-[linear-gradient(155deg,color-mix(in_oklab,var(--color-fg)_12%,var(--color-tile)),var(--color-tile)_70%)]`}
    >
      {initials(game.name)}
    </span>
  );
}
