#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
tmp="$(mktemp -d)"
cleanup() {
    rm -rf -- "$tmp"
}
trap cleanup EXIT

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

test_repo="$tmp/repo with spaces"
script="$test_repo/scripts/initialize-format-tables.sh"
destination="$test_repo/crates/victory-formats/src/bcpack-tables.bin"
mock_bin="$tmp/mock-bin"
curl_calls="$tmp/curl-calls"
mkdir -p "$test_repo/scripts" "$(dirname -- "$destination")" "$mock_bin"
cp -- "$repo_root/scripts/initialize-format-tables.sh" "$script"
chmod +x "$script"

cat > "$mock_bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
output=
url=
while (($#)); do
    case "$1" in
        --output)
            output=$2
            shift 2
            ;;
        *)
            url=$1
            shift
            ;;
    esac
done
printf '%s\n' "$url" >> "$CURL_CALLS"
case "${MOCK_CURL_MODE:-good}" in
    good) printf 'pinned test tables\n' > "$output" ;;
    bad) printf 'incorrect test tables\n' > "$output" ;;
    *) exit 2 ;;
esac
MOCK

cat > "$mock_bin/sha256sum" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ $# == 2 && $1 == -- ]] || exit 2
if [[ $(<"$2") == 'pinned test tables' ]]; then
    printf '1F1D4760419202BB5672F0C428CE51E456B5D08FBAEC8F371D043FD94E01C36B  %s\n' "$2"
else
    printf '0000000000000000000000000000000000000000000000000000000000000000  %s\n' "$2"
fi
MOCK
chmod +x "$mock_bin/curl" "$mock_bin/sha256sum"
export CURL_CALLS="$curl_calls"
export PATH="$mock_bin:$PATH"

run_script() (
    cd /
    "$script"
)

run_script > "$tmp/download-output" 2>&1 ||
    fail 'valid downloaded tables were not installed'
[[ $(<"$destination") == 'pinned test tables' ]] ||
    fail 'the downloaded tables did not reach the destination'
[[ $(<"$curl_calls") == \
    'https://raw.githubusercontent.com/Marvisak/VIctory/2849477415a46f5a079c85cf3e64a3dff32f713c/crates/victory-formats/src/bcpack-tables.bin' ]] ||
    fail 'the pinned HTTPS URL was not requested'

# A verified existing file is left alone without another download.
run_script > "$tmp/existing-output" 2>&1 ||
    fail 'a valid existing table file was rejected'
[[ $(<"$curl_calls") == \
    'https://raw.githubusercontent.com/Marvisak/VIctory/2849477415a46f5a079c85cf3e64a3dff32f713c/crates/victory-formats/src/bcpack-tables.bin' ]] ||
    fail 'an existing file triggered a download'

# A bad existing file must fail and remain untouched.
printf 'keep this mismatch\n' > "$destination"
if run_script > "$tmp/mismatch-output" 2>&1; then
    fail 'a mismatched existing file was accepted'
fi
[[ $(<"$destination") == 'keep this mismatch' ]] ||
    fail 'a mismatched existing file was replaced'
[[ $(<"$curl_calls") == \
    'https://raw.githubusercontent.com/Marvisak/VIctory/2849477415a46f5a079c85cf3e64a3dff32f713c/crates/victory-formats/src/bcpack-tables.bin' ]] ||
    fail 'a mismatched existing file triggered a download'

# A bad download must leave no destination or temporary file behind.
rm -- "$destination"
if MOCK_CURL_MODE=bad run_script > "$tmp/download-mismatch-output" 2>&1; then
    fail 'a mismatched download was accepted'
fi
[[ ! -e $destination ]] || fail 'a mismatched download created the destination'
if compgen -G "$(dirname -- "$destination")/.bcpack-tables.*" >/dev/null; then
    fail 'a temporary download file was not cleaned up'
fi

printf 'PASS: pinned BCPack format-table bootstrap\n'
