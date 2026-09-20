/**
 * Cover and icon URLs, at the size they are actually drawn.
 *
 * The server keeps art at the size SteamGridDB served it (a 600×900 cover, an icon up to 1024 px).
 * Drawn in a 38 px tile, a browser shrinks that by ~16× with a cheap filter that reads only a few of
 * the source pixels, which shows as jagged, shimmering edges. `?w=` asks the server for a copy already
 * resampled properly (Lanczos, in linear light) at one of `ART_WIDTHS`; a `srcSet` lets the browser
 * pick the one that matches its pixel density. Anything that is not one of our own `/art/` URLs
 * (a `data:` preview, say) is returned untouched.
 *
 * The widths must match `ArtThumbnails.Widths` on the server — an unlisted width is simply ignored
 * there and the full-size original comes back.
 */
export const ART_WIDTHS = [48, 64, 96, 128, 192, 256, 384] as const;

const isArtUrl = (url: string) => url.startsWith('/art/');
const at = (url: string, w: number) => `${url}${url.includes('?') ? '&' : '?'}w=${w}`;

/** The URL of `url` at `w` px wide (one of `ART_WIDTHS`). */
export const artSrc = (url: string, w: number) => (isArtUrl(url) ? at(url, w) : url);

/** A `srcSet` over the given widths; pair it with `sizes` set to the CSS width the image is drawn at. */
export const artSrcSet = (url: string, widths: readonly number[]) =>
  isArtUrl(url) ? widths.map(w => `${at(url, w)} ${w}w`).join(', ') : undefined;
