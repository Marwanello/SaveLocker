# Database backups (snapshots)

The server keeps its **version history** in one SQLite database: which games exist, every version of every save, which machine uploaded it, open conflicts, machines, settings and the audit log. The save files themselves are separate zip archives on disk. Without the database, those archives are just unlabelled files. For that reason the server takes regular snapshots of the database on its own.

## What a snapshot contains, and what it does not

A snapshot is a complete, standalone copy of the database. The server makes it with SQLite's `VACUUM INTO`, which is safe to run while the server keeps working.

- **Included:** games, versions and their history, conflicts, machines, settings, enrollment records and the audit log.
- **Not included:** the save **archives** (`/data/archives/`), cover art and hosted agent installers. The Backups page shows how much space the archives take up, so you can see what a snapshot leaves out. Back up the archives folder along with your other data.

## When snapshots are taken

| Reason | When |
|---|---|
| **Nightly** | Every day at the configured hour (default 03:00, server time). If the newest snapshot is more than a day old when the server starts, for example because the box was off overnight, it takes one right away. |
| **Manual** | When you press **Back up now** on the Backups page. |
| **Before upgrade** | When the server starts on a **different build** from the one that last ran. It is taken *before* any database migration runs, which is the one time the server itself could damage the file. |

Only the newest snapshots are kept (7 by default), and older ones are deleted. A before-upgrade snapshot counts toward that number too.

You can change the schedule with environment variables on the container: `Backup__Enabled`, `Backup__RetentionCount` and `Backup__HourOfDay`. A setting saved from the console takes priority over the environment variable.

## Where they are

In the container, snapshots are stored in `/data/backups/` and named `savelocker-YYYYMMDD-HHMMSS.db`. Manual and before-upgrade snapshots have `-manual` or `-before-upgrade` added to the name. On the host, that folder sits inside the path you mapped to `/data`, for example `/mnt/user/appdata/savelocker/backups/` on unRAID.

## Downloading a snapshot

**Download** on the Backups page saves a snapshot to your computer. **A snapshot is as sensitive as the admin password.** It contains every machine's API-key hash, the admin password hash, the console's session-token hashes and, if it was set from the console, the SteamGridDB key in plain text. Every download is recorded in the audit log as `backup.download`.

## Restoring one by hand

1. **Stop the container.** Don't swap the file while the server is running.
2. In the folder mapped to `/data`, rename the current database (`savelocker.db`, or `localgamesync.db` on older installs) so it's kept, and delete any `-wal` / `-shm` files next to it.
3. Copy the snapshot you want into that folder and give it the database's name.
4. **Start the container.** Any versions uploaded after the snapshot was taken are no longer listed. Their archives are still on disk, but nothing refers to them anymore.

Afterwards, look over the Games page. For any game with a newer save on a machine than the one the snapshot knows about, push it again from that machine.
