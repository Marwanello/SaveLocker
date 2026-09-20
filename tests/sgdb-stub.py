#!/usr/bin/env python3
"""A stand-in SteamGridDB for exercising the console's artwork by hand — no API key, no network.

    python tests/sgdb-stub.py [--port 5216]

then start the test console pointed at it (the container reaches this PC as host.docker.internal):

    .\\tests\\testenv.ps1 up -Only console -ConsoleEnv `
        Art__ApiBaseUrl=http://host.docker.internal:5216/api/v2/, `
        Art__AllowedImageHosts=host.docker.internal, Art__AllowInsecureImageUrls=true

Any key is accepted, so: start the console with NO key, add some games, then paste anything into
Configuration -> SteamGridDB and watch the games fill in. See "Testing artwork" in docs/Build and Run.md.

What it serves, for EVERY game name (the id is derived from the name, so it is stable):
  * covers  - 12, spread over two API pages of 7 and 5 (deliberately not the console's five per page),
              600x900 with fine diagonal lines and rings, which is what makes browser aliasing visible.
  * icons   - 8 of 256x256; the odd ones have transparent corners, the even ones none, and the FIRST is
              transparent - so the console's "prefer an opaque icon" rule is what decides the default.
  * no heroes, no logos.

The test suite (tests/run-console-security-tests.ps1) hosts its own, differently-shaped stub for
assertions; this one is for looking at.
"""
import argparse
import hashlib
import json
import math
import re
import struct
import zlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, unquote, urlparse

COVER_PAGES = [range(1, 8), range(8, 13)]   # 7 then 5
COVER_TOTAL = 12
ICON_COUNT = 8


def png(width, height, pixel):
    """A 32-bit RGBA PNG from pixel(x, y) -> (r, g, b, a). Pure stdlib."""
    rows = []
    for y in range(height):
        row = bytearray(b"\x00")
        for x in range(width):
            row += bytes(pixel(x, y))
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 6))
            + chunk(b"IEND", b""))


def hue(seed):
    """A base colour from a number - each cover and icon looks different."""
    h = int(hashlib.sha1(str(seed).encode()).hexdigest()[:6], 16)
    return (60 + (h >> 16) % 150, 60 + (h >> 8) % 150, 60 + h % 150)


_cache = {}


def cover(game, n, w=600, h=900):
    key = ("c", game, n, w)
    if key not in _cache:
        r0, g0, b0 = hue(f"{game}-{n}")
        cx, cy = w * 0.5, h * 0.38

        def px(x, y):
            t = y / h
            r, g, b = int(r0 * (1 - t) + 20 * t), int(g0 * (1 - t) + 30 * t), int(b0 * (1 - t) + 60 * t)
            # Fine 1px diagonals and thin rings: high-frequency detail a browser downscale mangles.
            if (x + y) % max(5, int(w / 60)) == 0:
                return (245, 232, 170, 255)
            d = math.hypot(x - cx, y - cy)
            if int(d) % max(6, int(w / 40)) == 0 and d < w * 0.42:
                return (255, 255, 255, 255)
            return (r, g, b, 255)

        _cache[key] = png(w, h, px)
    return _cache[key]


def icon(game, n, size=256):
    key = ("i", game, n)
    if key not in _cache:
        r0, g0, b0 = hue(f"{game}-icon-{n}")
        transparent = n % 2 == 1
        c = size / 2

        def px(x, y):
            # Odd icons are cut out to a disc, so the corners are transparent; even icons fill the square.
            if transparent and math.hypot(x - c, y - c) > size * 0.46:
                return (0, 0, 0, 0)
            if (x + y) % 9 == 0:
                return (255, 255, 255, 255)
            return (r0, g0, b0, 255)

        _cache[key] = png(size, size, px)
    return _cache[key]


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        print("%s %s" % (self.command, self.path))

    def _json(self, obj):
        body = json.dumps(obj).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _image(self, data):
        self.send_response(200)
        self.send_header("Content-Type", "image/png")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        url = urlparse(self.path)
        path, query = url.path, parse_qs(url.query)
        base = "http://" + self.headers.get("Host", "localhost")

        if m := re.fullmatch(r"/api/v2/search/autocomplete/(.+)", path):
            name = unquote(m.group(1))
            gid = int(hashlib.sha1(name.lower().encode()).hexdigest()[:6], 16) % 90000 + 1000
            return self._json({"success": True, "data": [{"id": gid, "name": name}]})

        if m := re.fullmatch(r"/api/v2/(grids|heroes|logos|icons)/game/(\d+)", path):
            kind, gid = m.group(1), int(m.group(2))
            page = int((query.get("page") or ["0"])[0])
            if kind == "grids":
                ns = COVER_PAGES[page] if page < len(COVER_PAGES) else []
                data = [{"id": gid * 100 + n, "url": f"{base}/img/grid/{gid}/{n}.png",
                         "thumb": f"{base}/img/grid/{gid}/{n}.png?thumb=1",
                         "width": 600, "height": 900, "author": {"name": f"artist{n}"}} for n in ns]
                return self._json({"success": True, "page": page, "total": COVER_TOTAL, "data": data})
            if kind == "icons":
                ns = range(1, ICON_COUNT + 1) if page == 0 else []
                data = [{"id": gid * 100 + n, "url": f"{base}/img/icon/{gid}/{n}.png",
                         "thumb": f"{base}/img/icon/{gid}/{n}.png",
                         "width": 256, "height": 256, "author": {"name": f"artist{n}"}} for n in ns]
                return self._json({"success": True, "page": page, "total": ICON_COUNT, "data": data})
            return self._json({"success": True, "page": page, "total": 0, "data": []})

        if m := re.fullmatch(r"/img/grid/(\d+)/(\d+)\.png", path):
            small = "thumb" in query
            return self._image(cover(int(m.group(1)), int(m.group(2)), *((200, 300) if small else (600, 900))))
        if m := re.fullmatch(r"/img/icon/(\d+)/(\d+)\.png", path):
            return self._image(icon(int(m.group(1)), int(m.group(2))))

        self.send_response(404)
        self.end_headers()


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--port", type=int, default=5216)
    args = ap.parse_args()
    print(f"stub SteamGridDB on 0.0.0.0:{args.port}  (any API key is accepted)")
    ThreadingHTTPServer(("0.0.0.0", args.port), Handler).serve_forever()
