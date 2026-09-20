#!/bin/sh
# Container entrypoint: run the server as an unprivileged user, without breaking an existing install.
#
# The image used to run the server as root. Switching it to a plain `USER app` would have looked like
# the whole fix and been a regression for every existing deployment: /data is a mounted share whose
# database and archives were created BY root, and a non-root process cannot write files it does not
# own — the server would come up on the first upgrade and fail on its first write.
#
# So this starts as root just long enough to hand /data to the unprivileged user (once — later starts
# find nothing to fix and skip the walk), then drops privileges with setpriv and execs the server.
#
#   SAVELOCKER_UID / SAVELOCKER_GID   run as this user instead of the image's `app` (1654). On unRAID,
#                                     99 / 100 (nobody:users) keeps files owned like the rest of a share.
#   SAVELOCKER_DATA_DIR               the state directory (default /data; the Dockerfile's VOLUME)
#
# Already started unprivileged (compose `user:` / `docker run --user`)? Then there is nothing to fix up
# and nothing to drop: check /data is usable — saying how to fix it if not, because "unable to open
# database file" is a poor way to learn about file ownership — and run.
set -eu

DATA_DIR="${SAVELOCKER_DATA_DIR:-/data}"

if [ "$(id -u)" != "0" ]; then
    if [ ! -w "$DATA_DIR" ]; then
        echo "savelocker: $DATA_DIR is not writable by uid $(id -u) / gid $(id -g)." >&2
        echo "savelocker: give that user the directory (e.g. chown -R $(id -u):$(id -g) on the host path" >&2
        echo "savelocker: mounted at $DATA_DIR), or start the container as root and let it do this itself." >&2
        exit 1
    fi
    exec "$@"
fi

uid="${SAVELOCKER_UID:-$(id -u app)}"
gid="${SAVELOCKER_GID:-$(id -g app)}"

# Explicitly asked to stay root: honour it, nothing to drop.
if [ "$uid" = "0" ]; then
    exec "$@"
fi

mkdir -p "$DATA_DIR"

# Cheap check first. A directory the server cannot write into stops it creating archives; a database
# file it does not own stops it writing at all. Walking directories (a handful per game) plus the
# top-level files (the database and its -wal/-shm) finds either without statting every archive.
if [ -n "$(find "$DATA_DIR" -type d ! -uid "$uid" -print -quit)" ] \
    || [ -n "$(find "$DATA_DIR" -maxdepth 1 -type f ! -uid "$uid" -print -quit)" ]; then
    echo "savelocker: giving $DATA_DIR to uid $uid:$gid (one-time: it is not owned by that user yet, e.g. a fresh volume or an install from when this image ran as root)"
    # Not fatal: a read-only sub-mount must not stop the server starting. If something the server
    # genuinely needs to write is still unwritable, it fails loudly at that write.
    chown -R "$uid:$gid" "$DATA_DIR" || echo "savelocker: warning: could not change ownership of everything under $DATA_DIR" >&2
fi

if [ "$uid" = "$(id -u app)" ]; then export HOME=/home/app; else export HOME=/tmp; fi
exec setpriv --reuid="$uid" --regid="$gid" --clear-groups "$@"
