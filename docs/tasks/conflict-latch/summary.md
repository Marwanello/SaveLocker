# A Linux agent's "conflict still unresolved" latch outlives the conflict

Not started. Moved out of `Backlog.md` on 2026-10-01; the notes below are as they stood there.

Seen on the WSL test agent 2026-09-28: with no open conflict on the server, every push logged "CONFLICT still unresolved — upload paused after 3 rejected attempts; no archive was sent" and then "Conflict Game pushed — latest save …", and the console's Push command read Done. Two things to look at: when the latch clears (a resolve made while the agent was down, or in a rig reset, may never reach it), and a push result that claims success when nothing was uploaded. Not investigated further.
