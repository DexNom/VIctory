Julia's Launcher 0.3.11: fewer crashes on 16 GB PCs, and your saves backed up before every game.

- **Fewer crashes when memory runs short.** On PCs with 16 GB of RAM, Windows sometimes refused VIctory a 64 MB buffer for a moment, and the game closed ("no host list for a state prelude", "device removed"). Players' logs showed it on RX 9070 XT, RX 6600, RX 6700 XT and RTX 4070. VIctory now lets go of the empty memory blocks it keeps in reserve (up to 4 GB of the card's and 512 MB of the PC's) and asks again before giving up.
- **Save backups.** Your saves are copied before every game (only when they changed; the newest 15 are kept). Hardware & ID > Game saves > **Restore a backup...** brings any of them back, and keeps your current saves in a backup too.
- Belobug's AMD engine is used only on Radeon cards and at most at 1080p: at 1440p and 4K it closed at start.

Is it safe? [docs/SAFETY.md](https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md) · [Code signing policy](https://github.com/felicienradio/cold-project-vi/blob/main/docs/CODE-SIGNING-POLICY.md)
