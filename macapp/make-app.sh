#!/bin/bash
# Build PPIDE and wrap it into a double-clickable macOS .app bundle.
set -e
cd "$(dirname "$0")"

swift build -c release

APP="PPIDE.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/release/PPIDE "$APP/Contents/MacOS/PPIDE"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>PPIDE</string>
  <key>CFBundleDisplayName</key><string>PP IDE</string>
  <key>CFBundleIdentifier</key><string>com.pp.ide</string>
  <key>CFBundleExecutable</key><string>PPIDE</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

echo "Built $APP"
open "$APP"
