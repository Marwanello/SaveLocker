#!/usr/bin/env bash
# Save-path autodetection harness.
#
# Answers the question "can users trust the path we set?" with a number instead of an anecdote.
# Materialises dummy save trees at the paths the REAL Ludusavi manifest claims, then scores the
# production PathResolver against them.
#
# No Steam, no Proton, no GPU and no Steam Deck: the resolver reads a token map and the filesystem,
# and both are faked. Runs fine under WSL.
#
# MUST be run from a Linux filesystem (~/), never /mnt/c: DrvFs is case-INSENSITIVE, and a
# case-sensitivity miss is exactly one of the Deck-only failures this harness exists to catch.
# A green run on DrvFs would be a fiction.
#
# Usage: tests/detection/run-detection-tests.sh [--sample N] [--seed S] [--offline]
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scratch="${repo_root}/.verify-detection"
manifest="${scratch}/manifest.yaml"
proj="${here}/SaveLocker.DetectionHarness/SaveLocker.DetectionHarness.csproj"

sample=300
seed=1
offline=0
while [ $# -gt 0 ]; do
  case "$1" in
    --sample) sample="$2"; shift 2 ;;
    --seed)   seed="$2";   shift 2 ;;
    --offline) offline=1;  shift ;;
    *) echo "unknown option: $1"; exit 2 ;;
  esac
done

case "${repo_root}" in
  /mnt/*) echo "REFUSING: ${repo_root} is a Windows drive (DrvFs, case-insensitive). Run from the WSL ext4 home."; exit 2 ;;
esac

mkdir -p "${scratch}"

# The manifest is cached rather than re-fetched: it is 17 MB, and a sweep whose input silently
# changes underneath it cannot be compared against the previous run.
if [ ! -s "${manifest}" ] && [ "${offline}" = "0" ]; then
  echo "Fetching Ludusavi manifest (~17 MB)…"
  curl -fsSL -o "${manifest}" \
    "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml" \
    || { echo "Fetch failed. Re-run with the file placed at ${manifest}."; exit 2; }
fi
[ -s "${manifest}" ] || { echo "No manifest at ${manifest} (and --offline was set)."; exit 2; }

echo "Building harness…"
dotnet build "${proj}" -c Release --nologo -v quiet -o "${scratch}/bin" || exit 2

run() { "${scratch}/bin/detection-harness" "$@" --manifest "${manifest}" --root "${scratch}/fixtures"; }

echo
echo "===== Coverage (analytic, whole manifest) ====="
run coverage || exit 2

echo
echo "===== Sweep (${sample} games, seed ${seed}) ====="
run sweep --sample "${sample}" --seed "${seed}" || exit 2

echo
echo "===== Pinned regression cases ====="
run pinned --cases "${here}/pinned-cases.tsv"
status=$?

echo
[ "${status}" = "0" ] && echo "Detection harness: PASS" || echo "Detection harness: FAIL"
exit "${status}"
