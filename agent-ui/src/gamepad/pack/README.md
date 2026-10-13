# Input glyph pack

The controller glyphs under this folder come from **Julio Cacko's "Free Input Pack"**
(Universal Version): <https://juliocacko.gumroad.com/l/free-input-pack>

The files were copied from the pack's `Source/` directory, with normalized names, by
`agent-ui/mockup/copy-glyphpack.cjs` — run it again only to pick up a new pack version:

```
node mockup/copy-glyphpack.cjs "D:\Projects\SaveLocker\Assets\Universal Version\Source"
```

- `xbox/` — the pack's **XGamepad** set
- `ps/` — the pack's **P5Gamepad** set
- Per set: `default/ light/ alt/ alt2/ retro/` (the pack's Default / Light / Alt / Alt 2 / Retro)
- Per style: the White (monochrome) files for every control, plus `a|b|x|y-color.png` for the
  coloured faces, which the UI offers as an optional tone.

The pack is fetched by the user, not vendored from any package manager; keep this note with the
files if they are ever moved.
