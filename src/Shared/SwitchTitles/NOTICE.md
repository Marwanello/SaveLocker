# switch-titles.tsv.gz — data notice

`switch-titles.tsv.gz` (Switch title ID → game name and eShop icon) is adapted from
[blawar/titledb](https://github.com/blawar/titledb/tree/496875f0587f19b5402b7ab305a82e6077cb1c16)
(`US.en.json`, `GB.en.json`, `JP.ja.json`), licensed under the MIT License:

> MIT License
>
> Copyright (c) 2020 blawar
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
> rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
> persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
> Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
> OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Changes: only each base game's title ID, name and the hash of its eShop icon's URL are kept (no demos, updates or DLC); the first region that names a
title wins (US, then UK, then Japan).

Rebuild it with `Update-SwitchTitles.ps1` in this folder.
