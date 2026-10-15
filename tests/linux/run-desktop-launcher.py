#!/usr/bin/env python3
"""Run a .desktop launcher's Exec the way a desktop would, without a desktop.

  run-desktop-launcher.py <file.desktop>

GLib's own parser (Gio.DesktopAppInfo + GLib.shell_parse_argv) splits the Exec line when python3-gi
is installed — that is what GNOME uses, and KDE's parser agrees on the quoting these launchers use.
Without it, shlex stands in. Terminal=true is ignored (there is no terminal to open); stdin gets a
newline so the launcher's closing "Press Enter" returns. Exits with the launched command's status.
Used by CI (the release's installer) and tests/testenv-flow.sh (the sandboxed install flow).
"""
import configparser
import shlex
import subprocess
import sys


def argv_from_glib(path):
    try:
        import gi
        gi.require_version("Gio", "2.0")
        from gi.repository import Gio, GLib
    except (ImportError, ValueError):
        return None
    info = Gio.DesktopAppInfo.new_from_filename(path)
    if info is None:
        sys.exit(f"GLib refused {path} as a desktop entry")
    ok, argv = GLib.shell_parse_argv(info.get_commandline())
    return argv


def argv_from_shlex(path):
    entry = configparser.ConfigParser(interpolation=None, strict=False)
    entry.optionxform = str
    entry.read(path, encoding="utf-8")
    return shlex.split(entry["Desktop Entry"]["Exec"].replace("%%", "%"))


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    path = sys.argv[1]
    argv = argv_from_glib(path)
    parser = "GLib"
    if argv is None:
        argv, parser = argv_from_shlex(path), "shlex (python3-gi not installed)"
    print(f"== {path}: {len(argv)} arguments, parsed by {parser}", flush=True)
    sys.exit(subprocess.run(argv, input="\n", text=True).returncode)


if __name__ == "__main__":
    main()
