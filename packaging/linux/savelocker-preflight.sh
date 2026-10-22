#!/bin/sh
# savelocker.service's ExecStartPre: repair, then hand over to `savelocker apply-update`.
#
# This exists for the one failure the agent cannot handle itself: its own program files are what
# broke. A power cut seconds after an update installs can leave every copied file at 0 bytes
# (Gotchas.md, "an update's files came back empty"). The rollback that would fix that lives inside
# the binary that is now empty, so systemd fails with "Exec format error" on every start, forever.
# Plain sh, no agent code: it has to work when the agent does not.
#
# While an update is unconfirmed, update/previous/ still holds the old version. If the installed
# agent cannot start, move those files back. `apply-update`, run by the restored agent, then sees the
# unconfirmed update and finishes the rollback: it removes files only the failed version added and
# reports it to the console.
prefix="$(cd "$(dirname "$0")" && pwd)"
bin="${prefix}/savelocker"
previous="${prefix}/update/previous"

say() {
  echo "$1" >&2
  printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$1" >>"${prefix}/agent.log" 2>/dev/null
}

# Free enough to run on every start: no process, a stat per file. The crash leaves empty files, so
# an empty binary or assembly is the signature.
looks_intact() {
  [ -s "${bin}" ] || return 1
  for f in "${prefix}"/*.dll; do
    [ -e "${f}" ] || continue
    [ -s "${f}" ] || return 1
  done
  return 0
}

# Only paid for while there is something to restore, which is the one start after an update.
starts() {
  if command -v timeout >/dev/null 2>&1; then
    timeout 60 "${bin}" version >/dev/null 2>&1
  else
    "${bin}" version >/dev/null 2>&1
  fi
}

if [ -s "${previous}/savelocker" ]; then
  if ! looks_intact || ! starts; then
    say "preflight: the installed agent cannot start. Restoring the previous version from ${previous}."
    find "${previous}" -type f | while IFS= read -r f; do
      rel="${f#"${previous}"/}"
      dst="${prefix}/${rel}"
      mkdir -p "$(dirname "${dst}")" && rm -f "${dst}" && mv "${f}" "${dst}" \
        || say "preflight: could not restore ${rel}"
    done
    sync
    if starts; then
      say "preflight: previous version restored."
    else
      say "preflight: the restored version cannot start either. Reinstall the agent: tar -xzf savelocker-<version>-linux-x64.tar.gz && ./SaveLocker/install.sh"
    fi
  fi
elif ! looks_intact; then
  say "preflight: the agent's program files are damaged (empty) and there is no previous version to restore. Reinstall the agent: tar -xzf savelocker-<version>-linux-x64.tar.gz && ./SaveLocker/install.sh"
fi

exec "${bin}" apply-update "$@"
