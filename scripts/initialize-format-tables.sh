#!/usr/bin/env bash
set -euo pipefail

fail() {
    printf 'Error: %s\n' "$*" >&2
    exit 1
}

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P) ||
    fail 'cannot determine the script directory.'
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd -P) ||
    fail 'cannot determine the repository root.'

relative_path='crates/victory-formats/src/bcpack-tables.bin'
destination="$repo_root/$relative_path"
destination_dir=${destination%/*}
expected_hash='1F1D4760419202BB5672F0C428CE51E456B5D08FBAEC8F371D043FD94E01C36B'
source_url="https://raw.githubusercontent.com/Marvisak/VIctory/2849477415a46f5a079c85cf3e64a3dff32f713c/$relative_path"
temp_file=

cleanup() {
    if [[ -n $temp_file ]]; then
        rm -f -- "$temp_file"
    fi
}
trap cleanup EXIT

verify_hash() {
    local path=$1 digest_line actual
    if ! digest_line=$(sha256sum -- "$path"); then
        fail "could not calculate the SHA-256 for '$path'."
    fi
    actual=${digest_line:0:64}
    if [[ ${actual^^} != "$expected_hash" ]]; then
        fail "unexpected BCPack table SHA-256 for '$path'. Expected $expected_hash, received ${actual^^}."
    fi
}

if [[ -e $destination || -L $destination ]]; then
    verify_hash "$destination"
    printf 'Verified existing BCPack format tables.\n'
    exit 0
fi

command -v curl >/dev/null 2>&1 || fail 'curl is required to download the BCPack format tables.'
command -v sha256sum >/dev/null 2>&1 || fail 'sha256sum is required to verify the BCPack format tables.'
command -v mktemp >/dev/null 2>&1 || fail 'mktemp is required to download the BCPack format tables safely.'
command -v mv >/dev/null 2>&1 || fail 'mv is required to install the BCPack format tables.'
[[ -d $destination_dir ]] || fail "destination directory does not exist: $destination_dir"

if ! temp_file=$(mktemp "$destination_dir/.bcpack-tables.XXXXXXXXXX"); then
    fail "cannot create a temporary file in '$destination_dir'."
fi

if ! curl --fail --location --silent --show-error \
    --proto '=https' --proto-redir '=https' \
    --output "$temp_file" "$source_url"; then
    fail "could not download the BCPack format tables from $source_url."
fi

verify_hash "$temp_file"

# The temporary file is in the destination directory, so this is an atomic
# rename. -nT also prevents replacing a file that appeared after the first
# existence check.
if ! mv -nT -- "$temp_file" "$destination"; then
    fail "could not install the BCPack format tables at '$destination'."
fi

if [[ -e $temp_file ]]; then
    # A concurrent invocation installed a file first; accept it only if it is
    # the same pinned data, and leave it untouched otherwise.
    verify_hash "$destination"
    printf 'Verified existing BCPack format tables.\n'
    exit 0
fi

printf 'Verified and installed the BCPack format tables.\n'
