#!/usr/bin/env bash

# Install this launcher beside victory.exe and game-path.txt.
set -u
set -o pipefail

usage() {
    printf 'Usage: %s [--rt] [--warp] [--warm] [game executable]\n' "${0##*/}" >&2
}

log_file=
fail() {
    printf 'Error: %s\n' "$*" >&2
    if [[ -n "${log_file:-}" ]]; then
        printf 'Error: %s\n' "$*" >>"$log_file"
    fi
    exit 1
}

rt=0
warp=0
warm=0
game_arg=
game_arg_set=0
end_options=0

while (($#)); do
    if ((end_options)); then
        if ((game_arg_set)); then
            usage
            exit 2
        fi
        game_arg=$1
        game_arg_set=1
    else
        case "$1" in
            --rt) rt=1 ;;
            --warp) warp=1 ;;
            --warm) warm=1 ;;
            --)
                end_options=1
                shift
                continue
                ;;
            -*)
                usage
                printf 'Error: unknown option: %s\n' "$1" >&2
                exit 2
                ;;
            *)
                if ((game_arg_set)); then
                    usage
                    exit 2
                fi
                game_arg=$1
                game_arg_set=1
                ;;
        esac
    fi
    shift
done

install_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P) || {
    printf 'Error: cannot determine the launcher directory.\n' >&2
    exit 1
}
# Keep generated per-install data outside the source checkout, matching Setup.
checkout_dir=$install_dir
while :; do
    if [[ -e "$checkout_dir/.victory-root" || -L "$checkout_dir/.victory-root" ]]; then
        fail "install directory $install_dir is inside a source checkout (found $checkout_dir/.victory-root). Choose an install directory outside the checkout."
    fi
    [[ "$checkout_dir" == / ]] && break
    checkout_dir=${checkout_dir%/*}
    [[ -n "$checkout_dir" ]] || checkout_dir=/
done

runtime="$install_dir/victory.exe"
logs_dir="$install_dir/logs"
state_dir="$install_dir/state"
cache_dir="$install_dir/cache"
wineprefix="$install_dir/wineprefix"
action=run
((warm)) && action=warm

if ! mkdir -p -- "$logs_dir"; then
    printf 'Error: cannot create the log directory: %s\n' "$logs_dir" >&2
    exit 1
fi
if ! command -v mktemp >/dev/null 2>&1 || ! command -v date >/dev/null 2>&1; then
    printf 'Error: mktemp and date are required to create per-run logs.\n' >&2
    exit 1
fi
log_file=$(mktemp "$logs_dir/$action-$(date +%Y%m%d-%H%M%S)-$$-XXXXXXXX.log") || {
    printf 'Error: cannot create a log in %s.\n' "$logs_dir" >&2
    exit 1
}

printf 'Action: %s\nLauncher: %s\n' "$action" "${BASH_SOURCE[0]}" >>"$log_file"
printf 'Log: %s\n' "$log_file"

latest_file="$logs_dir/latest.txt"
if ((warm)); then
    latest_file="$logs_dir/latest-warm.txt"
fi
if ! printf '%s\n' "$log_file" >"$latest_file"; then
    fail "Could not update the latest-log pointer: $latest_file"
fi

if [[ ! -f "$runtime" ]]; then
    fail "VIctory runtime is missing: $runtime (place this launcher beside victory.exe)."
fi
if ! command -v umu-run >/dev/null 2>&1; then
    fail 'umu-run was not found on PATH. Install umu-launcher and try again.'
fi
if ! command -v script >/dev/null 2>&1; then
    fail 'script was not found on PATH. Install util-linux to attach Proton to this terminal.'
fi

if ((game_arg_set)); then
    game_input=$game_arg
else
    config="$install_dir/game-path.txt"
    if [[ ! -f "$config" ]]; then
        fail "Game path is missing. Pass a Linux game executable path or create $config."
    fi
    IFS= read -r game_input <"$config" || :
    game_input=${game_input%$'\r'}
    if [[ -z "$game_input" ]]; then
        fail "No game executable path is set in $config."
    fi
    # Match the Windows launcher: relative paths in game-path.txt are install-relative.
    if [[ "$game_input" != /* ]]; then
        game_input="$install_dir/$game_input"
    fi
fi

# This file is intended to contain a Linux path, not a Windows drive or UNC path.
if [[ "$game_input" == [A-Za-z]:* || "$game_input" == \\* ]]; then
    fail "The game path must be a Linux path (for example /mnt/games/title/game.exe), not a Windows path: $game_input"
fi
if ! command -v realpath >/dev/null 2>&1; then
    fail 'realpath is required to normalize the Linux game path.'
fi
if ! game_path=$(realpath -e -- "$game_input" 2>/dev/null); then
    fail "Game executable does not exist: $game_input"
fi
if [[ ! -f "$game_path" ]]; then
    fail "Game path is not a regular file: $game_path"
fi
game_name=${game_path##*/}
if [[ "$game_name" != *.[eE][xX][eE] ]]; then
    fail "Game path must name a Windows .exe file: $game_path"
fi
if ! mkdir -p -- "$state_dir" "$cache_dir" "$wineprefix"; then
    fail 'Could not create this install’s state, cache, or Wine prefix directories.'
fi

# Wine's default Z: mapping exposes the Linux filesystem root, so Linux absolute
# paths passed to the Windows runtime must be rewritten as Z:\... DOS paths.
to_wine_path() {
    local linux_path=$1
    [[ "$linux_path" == /* ]] || return 1
    # Reject names that DOS treats as separators, drive syntax, or wildcards.
    case "$linux_path" in
        *\\*|*:*|*'?'*|*'*'*|*'"'*|*'<'*|*'>'*|*'|'*) return 1 ;;
    esac
    printf 'Z:%s' "${linux_path//\//\\}"
}

if ! game_wine=$(to_wine_path "$game_path"); then
    fail "The Linux game path cannot be represented as a Wine DOS path: $game_path"
fi
game_parent=${game_path%/*}
[[ -n "$game_parent" ]] || game_parent=/
if ! game_parent_wine=$(to_wine_path "$game_parent"); then
    fail "The game install directory cannot be represented as a Wine DOS path: $game_parent"
fi
if ! state_wine=$(to_wine_path "$state_dir") || ! cache_wine=$(to_wine_path "$cache_dir"); then
    fail 'The install path cannot be represented as a Wine DOS path for per-install state/cache.'
fi

# These environment variables are consumed by the Windows runtime; keep them
# in Wine DOS form. WINEPREFIX, by contrast, is a Linux-side umu-run path.
export PROTONPATH=GE-Proton
export GAMEID=umu-default
export WINEPREFIX="$wineprefix"
export VICTORY_DATA_DIR="$state_wine"
export VICTORY_CACHE_DIR="$cache_wine"
if ((rt)); then
    export VICTORY_BVH_BUILDER=1
    export VICTORY_BVH_QUARANTINE=0
    export VICTORY_ACTIVATE_RAYS=1
    export VICTORY_RT_RESOLVE=1
fi
if ((warp)); then
    export VICTORY_D3D12_WARP=1
fi
if [[ -z "${VICTORY_DEBUG_HUD+x}" ]]; then
    export VICTORY_DEBUG_HUD=1
fi

{
    printf 'Runtime: %s\nGame: %s\nWINEPREFIX: %s\n' \
        "$runtime" "$game_path" "$wineprefix"
    printf 'Options: rt=%s warp=%s warm=%s\n' "$rt" "$warp" "$warm"
} >>"$log_file"

printf 'Starting %s; output is also saved to %s\n' "$action" "$log_file"
if ((warm)); then
    command=(umu-run start.exe /b /wait /unix "$runtime" warm "$game_parent_wine")
else
    command=(umu-run start.exe /b /wait /unix "$runtime" run "$game_wine" --execute --console series-x --unimplemented trap)
fi

cd -- "$install_dir" || fail "Could not enter the install directory: $install_dir"
# Proton's umu.exe helper detaches console-subsystem programs. Start the Linux
# executable through Wine's start.exe instead; /b avoids a new console, /wait
# keeps the launch attached and returns the runtime's status, and /unix handles
# the Linux executable path. script supplies a PTY and streams output to the log.
printf -v command_line '%q ' "${command[@]}"
SHELL="$BASH" script --quiet --return --flush --command "$command_line" /dev/null \
    2>&1 | tee -a "$log_file"
pipeline_status=("${PIPESTATUS[@]}")
child_status=${pipeline_status[0]:-1}
tee_status=${pipeline_status[1]:-0}

if ((tee_status != 0)); then
    printf 'Warning: could not write all runtime output to %s.\n' "$log_file" >&2
fi
printf 'VIctory exited with status %d\n' "$child_status" | tee -a "$log_file"
exit "$child_status"
