#!/bin/sh
# savelocker.service's ExecStartPre: repair, then hand over to `savelocker apply-update`.
#
# This exists for the one failure the agent cannot handle itself: its own program files are what
# broke. A power cut seconds after an update installs can leave every copied file at 0 bytes
# (Gotchas.md, "an update's files came back empty"). The rollback that would fix that lives inside
# the binary that is now empty, so systemd fails with "Exec format error" on every start, forever.
# Plain sh, no agent code: it has to work when the agent does not.
#
# While an update is unconfirmed (applied.json) or was cut off part-way (installing.json),
# update/previous/ holds the old files it replaced. If the installed agent cannot start, move those
# back. `apply-update`, run by the restored agent, then finishes the job: it rolls back an unconfirmed
# update (removing files only the failed version added, and reporting it to the console), or resumes
# the cut-off install.
prefix="$(cd "$(dirname "$0")" && pwd)"
bin="${prefix}/savelocker"
update="${prefix}/update"
previous="${update}/previous"

say() {
  echo "$1" >&2
  printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$1" >>"${prefix}/agent.log" 2>/dev/null
}

# Free enough to run on every start: no process, a stat per file. The crash leaves empty files, so
# an empty binary, assembly, native library or the runtime's own json is the signature.
looks_intact() {
  [ -s "${bin}" ] || return 1
  for f in "${prefix}"/*.dll "${prefix}"/*.so "${prefix}"/savelocker.*.json; do
    [ -e "${f}" ] || continue
    [ -s "${f}" ] || return 1
  done
  return 0
}

# Only paid for while an update is pending, which is the one start after it installs.
starts() {
  if command -v timeout >/dev/null 2>&1; then
    timeout 60 "${bin}" version >/dev/null 2>&1
  else
    "${bin}" version >/dev/null 2>&1
  fi
}

# Without a marker, a previous/ is a leftover the agent failed to delete, not a rollback copy:
# restoring it would put back whatever version it happens to hold.
pending() {
  [ -e "${update}/applied.json" ] || [ -e "${update}/installing.json" ]
}

# Any file, not just the binary: an install cut off before it reached the binary has still replaced
# assemblies, and those old ones are only in previous/.
has_previous() {
  [ -n "$(find "${previous}" -type f 2>/dev/null | head -n 1)" ]
}

if pending && has_previous; then
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
