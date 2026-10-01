#!/usr/bin/env bash
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${1:-$project_dir/dist/macos-arm64}"
dotnet_executable="${DOTNET_EXE:-dotnet}"
app_path="$output_dir/Jumu.app"
zip_path="$output_dir/Jumu-0.4.4-mac-arm64.zip"

if [[ -e "$app_path" || -e "$zip_path" ]]; then
  echo "目标文件已存在；请指定一个新的输出目录，避免覆盖已有应用。" >&2
  exit 1
fi

mkdir -p "$output_dir/publish"
"$dotnet_executable" publish "$project_dir/src/Scribe.Desktop/Scribe.Desktop.csproj" \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$output_dir/publish"

mkdir -p "$app_path/Contents/MacOS"
mkdir -p "$app_path/Contents/Resources"
install -m 755 "$output_dir/publish/Jumu" "$app_path/Contents/MacOS/Jumu"
install -m 644 "$project_dir/packaging/macos/Info.plist" "$app_path/Contents/Info.plist"
install -m 644 "$project_dir/src/Scribe.Desktop/Assets/Jumu.icns" "$app_path/Contents/Resources/Jumu.icns"
plutil -lint "$app_path/Contents/Info.plist"

# Apple Silicon 需要完整有效的应用包签名，而不只是可执行文件的签名。
# 这是本地临时 ad-hoc 签名；正式分发仍需 Developer ID 签名和公证。
codesign --force --sign - --timestamp=none \
  --identifier local.renpyscribe.jumu "$app_path"
codesign --verify --deep --strict --verbose=2 "$app_path"

ditto --norsrc --noextattr --noqtn --noacl \
  -c -k --keepParent "$app_path" "$zip_path"
unzip -t "$zip_path"
echo "应用：$app_path"
echo "压缩包：$zip_path"
echo "注意：此包未获 Apple Developer ID 签名或公证，不能保证通过 Gatekeeper。"
