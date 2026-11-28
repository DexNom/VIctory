# Steam Deck (SteamOS, Linux, AMD)

What it took to run DexNom RayTime on a Steam Deck under Wine, what works,
and where it stops. Tested on 2026-11-27 with a Steam Deck (Van Gogh APU,
16 GB shared memory), SteamOS 3.9, Wine 11.0 (Flathub `org.winehq.Wine`,
branch `stable-26.08`), vkd3d-proton and DXVK from Proton Experimental, RADV
(Mesa 26.2). Series S profile, ray tracing off.

## Where it gets to

| Stage | Result |
|---|---|
| Boot, first-boot screens (brightness, legal notices, warnings) | works, 30 fps, with the two-plane fix below |
| Account / player prompt, loading screen art | works |
| Loading the opening chapter | runs for minutes, then the Deck runs out of memory |
| World | not reached |

**The wall is memory, not a crash.** While the opening loads, the GPU side
grows past 7 GB of system memory mapped for the card (GTT, which cannot be
swapped), on top of about 8 GB for the title's own memory. The Deck has 16 GB
for everything. Without a cap the whole Deck froze; with the cap below only
the game is stopped. Getting further would need the runtime to keep much
less of the title's memory twice (title copy plus host GPU copy).

## Fixes in the code (this branch)

* **Series S menus were invisible** (`compose.rs`, `queue.rs`, `host.rs`).
  On Series S the title presents its scene and its interface as two planes
  (scene 1080p, interface 4K); the runtime showed only the first, so every
  menu, prompt and legal screen was blank. The interface plane is now
  composited over the scene. Ported from
  [VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA). The
  composer's root signature is built in code and its shaders are 5.0, so it
  also compiles with Wine's built-in `d3dcompiler_47`.
* **The title ended itself during loads** (`watchdog.rs`). When a wait on
  another GPU queue runs past the title's limit, its hang handler and its
  watchdog thread both fault on purpose. On a slow machine that is a slow
  frame, not a hang. Both stores are replaced so the wait goes on
  (`VICTORY_TITLE_WATCHDOG=1` keeps them). The handler's offset comes from
  [victory-linux-nvidia](https://github.com/dfmbrollins/victory-linux-nvidia);
  the watchdog thread's is new.

## Setup (no changes to SteamOS itself)

1. **Build** (Windows target, cross-compiled). SteamOS has no compiler, so
   use a [distrobox](https://distrobox.it) container (SteamOS ships it):

   ```sh
   distrobox create -n victory-build -i docker.io/library/archlinux:latest -Y
   distrobox enter victory-build -- sudo pacman -Syu --noconfirm base-devel clang lld llvm git
   distrobox enter victory-build -- bash -lc 'cargo install --locked cargo-xwin'
   # Fetch and verify the BCPack tables as Initialize-FormatTables.ps1 does
   # (same URL and SHA-256), then:
   distrobox enter victory-build -- bash -lc 'cd <checkout> && cargo xwin build --release -p victory --bins --target x86_64-pc-windows-msvc'
   ```

2. **Wine**: `flatpak install flathub app/org.winehq.Wine/x86_64/stable-26.08`.
   A `pacman` install does not survive SteamOS updates.

3. **A prefix** for the game, with vkd3d-proton and DXVK's `dxgi.dll` copied
   from `steamapps/common/Proton - Experimental/files/lib/wine/{vkd3d-proton,dxvk}/x86_64-windows/`
   into its `system32`, and the three programs in, say, `C:\DexNom`, with
   `game-path.txt` holding the `Z:\...` path of your dump's executable.

4. **Microsoft's `d3dcompiler_47`** (required):
   `flatpak run --env=WINEPREFIX=... --command=winetricks org.winehq.Wine -q d3dcompiler_47`.
   With Wine's own, some of the runtime's helper shaders (shader model 5.1
   with an inline root signature) fail to compile; the log then says
   `no host copies in the card's memory: the compare could not be made`.

5. **Shaders** ahead of time, translation only (pipelines are not kept on
   this host anyway, and building them stalled the warm-up for minutes):
   `victory.exe warm --no-pipelines Z:\path\to\your\dump`. About a minute.

6. **Memory**: enable the 16 GB swap file SteamOS sets up
   (`sudo systemctl start home-swapfile.swap`), and set *UMA Frame Buffer
   Size* to 1 GB in the Deck's BIOS (Volume Up + Power, Setup Utility,
   Advanced). That took usable RAM from 11.7 to 14.8 GB. Close Steam,
   browsers and chat apps before playing.

7. **Launch** with the script below.

```bash
#!/usr/bin/env bash
# DexNom RayTime on a Steam Deck through the Wine Flatpak.
MEMORY_MAX="${MEMORY_MAX:-12G}"   # only the game is stopped when memory runs out
SWAP_MAX="${SWAP_MAX:-8G}"
PREFIX=/var/data/dexnom           # the prefix, as the Flatpak sees it

rm -f "$HOME/.var/app/org.winehq.Wine/data/dexnom/drive_c/DexNom/vkd3d-proton.cache.write"
scopes() { systemctl --user list-units --type=scope --plain --no-legend 'app-flatpak-org.winehq.Wine-*' | awk '{print $1}' | sort; }
before=$(scopes)

# --filesystem=/tmp: Wine keeps the title's shared memory in /tmp; the
#   Flatpak's own /tmp is a 1.2 GB RAM disk, which filled and stalled loading.
# VKD3D_SHADER_CACHE_PATH=0: a killed run left vkd3d's cache lock behind and
#   the next run waited on it forever.
# VICTORY_TEXTURE_BUDGET_MB / VICTORY_CARD_BUFFERS_MB: smaller GPU-side copies.
flatpak run --filesystem=/tmp \
  --env=WINEPREFIX=$PREFIX --env=WINEDEBUG=-all --env=VKD3D_SHADER_CACHE_PATH=0 \
  --env=VICTORY_TEXTURE_BUDGET_MB=1024 --env=VICTORY_CARD_BUFFERS_MB=256 \
  --env=WINEDLLOVERRIDES="d3d12,d3d12core,dxgi=n,b" \
  org.winehq.Wine 'C:\DexNom\victory-play.exe' --no-rt --console series-s "$@" &
game=$!

# Flatpak moves itself into its own systemd scope; cap that scope.
for _ in $(seq 1 60); do
  scope=$(comm -13 <(echo "$before") <(scopes) | head -1)
  [[ -n "$scope" ]] && { systemctl --user set-property --runtime "$scope" MemoryMax="$MEMORY_MAX" MemorySwapMax="$SWAP_MAX"; break; }
  sleep 0.5
done
wait "$game"
```

## Notes

* **Input**: the first-boot screens wait for input, and only the focused
  window gets it. The keyboard works as a pad: Enter is A, Backspace B,
  Esc Menu, arrows the D-pad. In Desktop Mode the Deck's A button sends
  Enter.
* **Series X profile** crashed early on the Deck; use `--console series-s`.
* **`--local-gpu-memory`** made the menus slower (6 fps) and did not help.
* The GPU reads 0 % busy on menus and loading screens: they draw a few
  dozen things a frame. Loading is CPU-bound (BCPack decoding on four cores).
