#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
launcher_source="$repo_root/scripts/victory-linux"
if [[ ! -f $launcher_source ]]; then
    launcher_source="$repo_root/scripts/victory-linux.sh"
fi

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

assert_equal() {
    local expected=$1 actual=$2 description=$3
    [[ $actual == "$expected" ]] ||
        fail "$description (expected '$expected', got '$actual')"
}

assert_arg() {
    local args_file=$1 expected=$2 arg
    while IFS= read -r arg; do
        [[ $arg == "$expected" ]] && return 0
    done < "$args_file"
    fail "umu-run arguments did not include '$expected'"
}

assert_path_arg() {
    local args_file=$1 expected_path=$2 arg normalized
    while IFS= read -r arg; do
        normalized=${arg//\\//}
        [[ $normalized == "$expected_path" || $normalized == *"$expected_path" ]] &&
            return 0
    done < "$args_file"
    fail "umu-run arguments did not include the configured path '$expected_path'"
}

assert_argv_prefix() {
    local args_file=$1 expected arg index=0
    shift
    local -a expected_args=("$@")
    while IFS= read -r arg; do
        if ((index >= ${#expected_args[@]})); then
            return 0
        fi
        expected=${expected_args[index]}
        [[ $arg == "$expected" ]] ||
            fail "umu-run argument $index was '$arg', expected '$expected'"
        ((index += 1))
    done < "$args_file"
    fail "umu-run arguments ended before the expected prefix"
}

has_line() {
    local file=$1 expected=$2 line
    [[ -f $file ]] || return 1
    while IFS= read -r line; do
        line=${line%$'\r'}
        [[ $line == "$expected" ]] && return 0
    done < "$file"
    return 1
}

[[ -f $launcher_source ]] ||
    fail "launcher not found at scripts/victory-linux[.sh]"

tmp="$(mktemp -d)"
active_pid=
cleanup() {
    if [[ -n $active_pid ]]; then
        : > "$tmp/release"
        kill "$active_pid" 2>/dev/null || true
        wait "$active_pid" 2>/dev/null || true
    fi
    rm -rf -- "$tmp"
}
trap cleanup EXIT

mock_bin="$tmp/mock-bin"
capture_dir="$tmp/capture"
mkdir -p "$mock_bin" "$capture_dir"

cat > "$mock_bin/umu-run" <<'MOCK'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$@" > "$UMU_CAPTURE_DIR/args"
printf '%s\n' "${PROTONPATH-}" > "$UMU_CAPTURE_DIR/protonpath"
printf '%s\n' "${GAMEID-}" > "$UMU_CAPTURE_DIR/gameid"
printf '%s\n' "${WINEPREFIX-}" > "$UMU_CAPTURE_DIR/wineprefix"
printf '%s\n' "${VICTORY_DATA_DIR-}" > "$UMU_CAPTURE_DIR/data-dir"
printf '%s\n' "${VICTORY_CACHE_DIR-}" > "$UMU_CAPTURE_DIR/cache-dir"
printf '%s\n' "${VICTORY_BVH_BUILDER-}" > "$UMU_CAPTURE_DIR/bvh-builder"
printf '%s\n' "${VICTORY_BVH_QUARANTINE-}" > "$UMU_CAPTURE_DIR/bvh-quarantine"
printf '%s\n' "${VICTORY_ACTIVATE_RAYS-}" > "$UMU_CAPTURE_DIR/activate-rays"
printf '%s\n' "${VICTORY_RT_RESOLVE-}" > "$UMU_CAPTURE_DIR/rt-resolve"
printf '%s\n' "${VICTORY_D3D12_WARP-}" > "$UMU_CAPTURE_DIR/warp"
printf '%s\n' "${VICTORY_DEBUG_HUD-}" > "$UMU_CAPTURE_DIR/debug-hud"
if [[ -t 1 ]]; then
    printf 'yes\n' > "$UMU_CAPTURE_DIR/stdout-tty"
else
    printf 'no\n' > "$UMU_CAPTURE_DIR/stdout-tty"
fi
if [[ -t 2 ]]; then
    printf 'yes\n' > "$UMU_CAPTURE_DIR/stderr-tty"
else
    printf 'no\n' > "$UMU_CAPTURE_DIR/stderr-tty"
fi
: > "$UMU_CAPTURE_DIR/called"

if [[ ${UMU_HOLD_OPEN:-0} == 1 ]]; then
    printf 'stream-ready\n'
    while [[ ! -e ${UMU_RELEASE_FILE:?} ]]; do
        sleep 0.01
    done
else
    printf 'mock-umu-stdout\n'
fi
exit "${UMU_EXIT_CODE:-0}"
MOCK
chmod +x "$mock_bin/umu-run"

export UMU_CAPTURE_DIR="$capture_dir"
export PATH="$mock_bin:$PATH"

create_install() {
    local install=$1
    mkdir -p "$install/game files"
    cp -- "$launcher_source" "$install/victory-linux"
    chmod +x "$install/victory-linux"
    : > "$install/victory.exe"
    : > "$install/game files/game.exe"
    chmod +x "$install/victory.exe" "$install/game files/game.exe"
    printf '%s\n' "$install/game files/game.exe" > "$install/game-path.txt"
}

clear_capture() {
    rm -f -- "$capture_dir/args" "$capture_dir/protonpath" \
        "$capture_dir/gameid" "$capture_dir/wineprefix" \
        "$capture_dir/data-dir" "$capture_dir/cache-dir" \
        "$capture_dir/bvh-builder" "$capture_dir/bvh-quarantine" \
        "$capture_dir/activate-rays" "$capture_dir/rt-resolve" \
        "$capture_dir/warp" "$capture_dir/debug-hud" \
        "$capture_dir/stdout-tty" "$capture_dir/stderr-tty" \
        "$capture_dir/called"
}

wine_path() {
    printf 'Z:%s' "${1//\//\\}"
}

run_launcher() (
    local install=$1
    shift
    cd -- "$install"
    "$install/victory-linux" "$@"
)

install_one="$tmp/install one"
create_install "$install_one"
expected_game="$install_one/game files/game.exe"
expected_runtime="$install_one/victory.exe"
unset VICTORY_BVH_BUILDER VICTORY_BVH_QUARANTINE VICTORY_ACTIVATE_RAYS \
    VICTORY_RT_RESOLVE VICTORY_D3D12_WARP VICTORY_DEBUG_HUD

# Default invocation reads game-path.txt, selects GE-Proton, and supplies a
# WINEPREFIX associated with this install.
clear_capture
run_launcher "$install_one" > "$tmp/default-output" 2>&1
[[ -e $capture_dir/called ]] || fail "umu-run was not invoked"
assert_equal "GE-Proton" "$(cat "$capture_dir/protonpath")" "PROTONPATH"
assert_equal "umu-default" "$(cat "$capture_dir/gameid")" "GAMEID"
prefix_one="$(cat "$capture_dir/wineprefix")"
[[ -n $prefix_one ]] || fail "WINEPREFIX was empty"
assert_equal "$install_one/wineprefix" "$prefix_one" "per-install WINEPREFIX"
assert_equal "$(wine_path "$install_one/state")" \
    "$(cat "$capture_dir/data-dir")" "Wine-formatted data directory"
assert_equal "$(wine_path "$install_one/cache")" \
    "$(cat "$capture_dir/cache-dir")" "Wine-formatted cache directory"
assert_equal "1" "$(cat "$capture_dir/debug-hud")" "default debug HUD"
assert_equal "yes" "$(cat "$capture_dir/stdout-tty")" \
    "umu-run stdout is attached to a PTY"
assert_equal "yes" "$(cat "$capture_dir/stderr-tty")" \
    "umu-run stderr is attached to a PTY"
assert_argv_prefix "$capture_dir/args" start.exe /b /wait /unix "$expected_runtime" run
assert_path_arg "$capture_dir/args" "$expected_game"
has_line "$tmp/default-output" "mock-umu-stdout" ||
    fail "umu-run stdout was not passed through"

# Each supported launcher option reaches the runtime invocation.
for flag in --rt --warp --warm; do
    clear_capture
    run_launcher "$install_one" "$flag" > "$tmp/flag-output" 2>&1
    [[ -e $capture_dir/called ]] || fail "umu-run was not invoked for $flag"
    assert_argv_prefix "$capture_dir/args" start.exe /b /wait /unix "$expected_runtime"
    if [[ $flag == --warm ]]; then
        assert_arg "$capture_dir/args" "warm"
        assert_path_arg "$capture_dir/args" "${expected_game%/*}"
    else
        assert_arg "$capture_dir/args" "run"
        assert_path_arg "$capture_dir/args" "$expected_game"
    fi
    assert_equal "GE-Proton" "$(cat "$capture_dir/protonpath")" "PROTONPATH for $flag"
    assert_equal "$prefix_one" "$(cat "$capture_dir/wineprefix")" \
        "stable WINEPREFIX for one install"
    case "$flag" in
        --rt)
            assert_equal "1" "$(cat "$capture_dir/bvh-builder")" "RT BVH builder"
            assert_equal "0" "$(cat "$capture_dir/bvh-quarantine")" "RT quarantine"
            assert_equal "1" "$(cat "$capture_dir/activate-rays")" "RT activation"
            assert_equal "1" "$(cat "$capture_dir/rt-resolve")" "RT resolve"
            ;;
        --warp)
            assert_equal "1" "$(cat "$capture_dir/warp")" "WARP flag"
            ;;
    esac
done

# A second install gets its own prefix rather than sharing the first install's.
install_two="$tmp/install two"
create_install "$install_two"
clear_capture
run_launcher "$install_two" > "$tmp/second-output" 2>&1
prefix_two="$(cat "$capture_dir/wineprefix")"
[[ -n $prefix_two ]] || fail "WINEPREFIX was empty for the second install"
[[ $prefix_two != "$prefix_one" ]] ||
    fail "separate installs shared a WINEPREFIX"

# The launcher must return the status from umu-run unchanged.
clear_capture
set +e
UMU_EXIT_CODE=37 run_launcher "$install_one" > "$tmp/return-code-output" 2>&1
status=$?
set -e
assert_equal "37" "$status" "launcher return code"

# Prove output is visible while umu-run is still running, not only after it exits.
clear_capture
rm -f -- "$tmp/release"
UMU_HOLD_OPEN=1 UMU_RELEASE_FILE="$tmp/release" \
    run_launcher "$install_one" > "$tmp/stream-output" 2>&1 &
active_pid=$!
streamed=0
for ((attempt = 0; attempt < 500; attempt++)); do
    if has_line "$tmp/stream-output" "stream-ready"; then
        streamed=1
        break
    fi
    kill -0 "$active_pid" 2>/dev/null ||
        fail "launcher exited before streaming umu-run output"
    sleep 0.01
done
[[ $streamed == 1 ]] || fail "umu-run output was not streamed while it was running"
: > "$tmp/release"
wait "$active_pid" || fail "streaming invocation failed"
active_pid=

# Reject a configured game that does not exist without invoking umu-run.
clear_capture
rm -- "$install_one/game files/game.exe"
set +e
run_launcher "$install_one" > "$tmp/missing-game-output" 2>&1
status=$?
set -e
[[ $status != 0 ]] || fail "missing game was accepted"
[[ ! -e $capture_dir/called ]] || fail "umu-run ran with a missing game"
: > "$install_one/game files/game.exe"
chmod +x "$install_one/game files/game.exe"

# Reject a missing runtime before invoking umu-run.
clear_capture
rm -- "$install_one/victory.exe"
set +e
run_launcher "$install_one" > "$tmp/missing-runtime-output" 2>&1
status=$?
set -e
[[ $status != 0 ]] || fail "missing runtime was accepted"
[[ ! -e $capture_dir/called ]] || fail "umu-run ran with a missing runtime"
: > "$install_one/victory.exe"
chmod +x "$install_one/victory.exe"

# Keep generated runtime state outside a source checkout. A sentinel in an
# ancestor models the repository's .victory-root file.
fake_checkout="$tmp/fake-checkout"
checkout_install="$fake_checkout/runtime"
mkdir -p "$checkout_install"
: > "$fake_checkout/.victory-root"
cp -- "$launcher_source" "$checkout_install/victory-linux"
chmod +x "$checkout_install/victory-linux"
: > "$checkout_install/victory.exe"
clear_capture
set +e
(
    cd -- "$checkout_install"
    ./victory-linux > "$tmp/in-checkout-output" 2>&1
)
status=$?
set -e
[[ $status != 0 ]] || fail "an in-checkout runtime install was accepted"
[[ ! -e $checkout_install/logs && ! -e $checkout_install/state &&
    ! -e $checkout_install/cache && ! -e $checkout_install/wineprefix ]] ||
    fail "an in-checkout runtime created generated data"
[[ ! -e $capture_dir/called ]] ||
    fail "umu-run was invoked from an in-checkout runtime"

# Use an isolated PATH without umu-run to check the missing-launcher case even
# on machines where umu-run may be installed globally.
no_umu_bin="$tmp/no-umu-bin"
mkdir -p "$no_umu_bin"
for utility in dirname basename readlink realpath cat mkdir sleep env date mktemp; do
    utility_path="$(type -P "$utility" || true)"
    if [[ -n $utility_path && ! -e $no_umu_bin/$utility ]]; then
        ln -s -- "$utility_path" "$no_umu_bin/$utility"
    fi
done
clear_capture
set +e
(
    cd -- "$install_one"
    PATH="$no_umu_bin" /bin/bash "$install_one/victory-linux"
) > "$tmp/missing-umu-output" 2>&1
status=$?
set -e
[[ $status != 0 ]] || fail "missing umu-run was accepted"
[[ ! -e $capture_dir/called ]] || fail "umu-run was invoked from the isolated PATH"

printf 'PASS: Linux launcher contract\n'
