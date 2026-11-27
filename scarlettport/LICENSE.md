# Licensing

This repository combines two GPL runtimes with this project's own scripts and docs. Each part keeps
its own license:

| Path | License | Copyright |
|---|---|---|
| `runtime/xcl/` | GPL-2.0-only (see `runtime/xcl/LICENSE`) | the XCL authors (github.com/ivall/xcl) |
| `engine/` (the ScarlettPort runtime) | GPL-3.0-or-later (see `engine/LICENSE`) | the VIctory authors (github.com/Marvisak/VIctory), the authors of the forks and pull requests merged in, and this project's contributors; see `CREDITS.md` and `engine/UPSTREAM.md` |
| everything else (`scripts/`, `docs/`, `patches/`, `README.md`, …) | GPL-3.0-or-later | this project's contributors |

The two runtimes are separate programs shipped side by side; neither links to the other.

This project contains no game code, assets or data, and no console SDK material.
