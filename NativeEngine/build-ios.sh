#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
    echo "The iOS duel engine requires macOS and the Xcode iPhoneOS SDK." >&2
    exit 1
fi

engine_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(dirname "$engine_dir")"
core_revision=c2e0b697e031d3560c077c9cd2cc76474969dedf
core_dir="$engine_dir/build/ios-sources/$core_revision"
build_dir="$engine_dir/build/ios-device"
plugin_dir="$project_dir/Assets/Plugins/iOS"
sdk_path="$(xcrun --sdk iphoneos --show-sdk-path)"
deployment_target="$(awk '/iOSTargetOSVersionString:/ { print $2 }' "$project_dir/ProjectSettings/ProjectSettings.asset")"

mkdir -p "$core_dir"
if [[ ! -d "$core_dir/.git" ]]; then
    git -C "$core_dir" init -q
fi
if [[ "$(git -C "$core_dir" rev-parse HEAD 2>/dev/null || true)" != "$core_revision" ]]; then
    git -C "$core_dir" fetch --depth 1 https://github.com/edo9300/ygopro-core.git "$core_revision"
    git -C "$core_dir" checkout --detach FETCH_HEAD
fi
git -C "$core_dir" submodule update --init --depth 1 -- lua/src

cmake -S "$engine_dir" -B "$build_dir" -G 'Unix Makefiles' \
    -DYGOPRO_CORE_DIR="$core_dir" \
    -DCMAKE_SYSTEM_NAME=iOS \
    -DCMAKE_OSX_SYSROOT="$sdk_path" \
    -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_OSX_DEPLOYMENT_TARGET="${deployment_target:?Missing iOS deployment target}" \
    -DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY \
    -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_dir" --target ygoengine --parallel "$(sysctl -n hw.logicalcpu)"

archive="$build_dir/libygo_native.a"
xcrun lipo -verify_arch arm64 "$archive"
mkdir -p "$plugin_dir"
install -m 644 "$archive" "$plugin_dir/libygo_native.a"
