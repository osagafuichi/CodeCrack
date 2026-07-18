#!/bin/bash
# Build PPIDE and wrap it into a double-clickable macOS .app bundle.
#
# Code signing + notarization (all OPTIONAL — steps no-op unless the credentials
# below are set, so this script works unchanged on a machine without them):
#
#   CODECRACK_SIGN_IDENTITY   Developer ID Application identity to codesign with,
#                             e.g. "Developer ID Application: Jane Dev (TEAMID1234)".
#                             When set: the bundle is deep-signed with the hardened
#                             runtime + PPIDE.entitlements. When unset: signing is
#                             skipped (unsigned bundle; fine for local dev).
#
#   Notarization additionally requires ALL THREE of:
#   CODECRACK_APPLE_ID        Apple ID email used for notarytool submission.
#   CODECRACK_TEAM_ID         Apple Developer Team ID (10-char), e.g. TEAMID1234.
#   CODECRACK_APP_PASSWORD    App-specific password for that Apple ID (NOT the real
#                             account password; create one at appleid.apple.com).
#   When all three are set (and signing ran): the app is submitted to Apple's
#   notary service and the ticket is stapled. When any is missing: notarization is
#   skipped with a printed notice.
#
#   CODECRACK_SKIP_LAUNCH     Set to assemble the bundle without opening the GUI
#                             (used by CI and headless builds).
set -e
cd "$(dirname "$0")"

swift build -c release

APP="PPIDE.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/release/PPIDE "$APP/Contents/MacOS/PPIDE"

# Copy SwiftPM resource bundles (e.g. Highlightr's highlight.js/CSS) so Bundle.module
# resolves them at runtime. Without this, Highlightr() returns nil and the app crashes.
shopt -s nullglob
for bundle in .build/release/*.bundle; do
  cp -R "$bundle" "$APP/Contents/Resources/"
done
shopt -u nullglob

# Bundle the CodeCrack Python engine into Resources so the shipped app is
# self-contained — Analyzer.swift discovers it via Bundle.main.resourceURL and no
# longer depends on a source checkout on the user's machine. Exclude caches/tests
# so we ship only the importable package + metadata.
echo "Bundling engine into $APP/Contents/Resources/engine ..."
ENGINE_SRC="$(cd .. && pwd)/engine"
rm -rf "$APP/Contents/Resources/engine"
mkdir -p "$APP/Contents/Resources/engine"
cp -R "$ENGINE_SRC/codecrack" "$APP/Contents/Resources/engine/codecrack"
cp "$ENGINE_SRC/pyproject.toml" "$APP/Contents/Resources/engine/pyproject.toml"
find "$APP/Contents/Resources/engine" -type d -name '__pycache__' -prune -exec rm -rf {} +

# Bundle an embedded CPython (with pytest) so the app doesn't need a system python3.
# The fetch is scripted + cached; see scripts/fetch-python-runtime.sh. The engine's
# execute stage runs `python -m pytest` via sys.executable, so this interpreter MUST
# carry pytest — the fetch script installs it.
echo "Preparing embedded Python runtime ..."
"$(cd .. && pwd)/scripts/fetch-python-runtime.sh"
PY_RUNTIME="$(cd .. && pwd)/build/python-runtime/python"
echo "Bundling Python runtime into $APP/Contents/Resources/python ..."
rm -rf "$APP/Contents/Resources/python"
cp -R "$PY_RUNTIME" "$APP/Contents/Resources/python"
# Prune bytecode caches so the bundle is smaller and reproducible.
find "$APP/Contents/Resources/python" -type d -name '__pycache__' -prune -exec rm -rf {} +

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

# --- Code signing (Developer ID Application + hardened runtime) --------------
# Guarded: only runs when CODECRACK_SIGN_IDENTITY is set. Signs inner Mach-O
# (the embedded interpreter + its dylibs/.so) first, then the outer bundle, all
# with the hardened runtime and our entitlements.
ENTITLEMENTS="PPIDE.entitlements"
if [ -n "${CODECRACK_SIGN_IDENTITY:-}" ]; then
  echo "Code signing $APP with '$CODECRACK_SIGN_IDENTITY' (hardened runtime) ..."
  # Sign every Mach-O binary inside the bundled runtime before the app itself.
  # (--deep is unreliable for embedded interpreters; sign inside-out explicitly.)
  find "$APP/Contents/Resources/python" \
    \( -type f \( -name '*.dylib' -o -name '*.so' \) \) -o \
    \( -type f -perm -u+x ! -name '*.py' \) 2>/dev/null | while read -r bin; do
      codesign --force --options runtime --timestamp \
        --sign "$CODECRACK_SIGN_IDENTITY" "$bin" || true
  done
  codesign --force --options runtime --timestamp \
    --entitlements "$ENTITLEMENTS" \
    --sign "$CODECRACK_SIGN_IDENTITY" "$APP"
  echo "Verifying signature ..."
  codesign --verify --deep --strict --verbose=2 "$APP"

  # --- Notarization (notarytool submit + staple) ----------------------------
  # Guarded: only runs when all three notarization creds are present.
  if [ -n "${CODECRACK_APPLE_ID:-}" ] && [ -n "${CODECRACK_TEAM_ID:-}" ] \
     && [ -n "${CODECRACK_APP_PASSWORD:-}" ]; then
    echo "Notarizing $APP ..."
    NOTARIZE_ZIP="PPIDE-notarize.zip"
    ditto -c -k --keepParent "$APP" "$NOTARIZE_ZIP"
    xcrun notarytool submit "$NOTARIZE_ZIP" \
      --apple-id "$CODECRACK_APPLE_ID" \
      --team-id "$CODECRACK_TEAM_ID" \
      --password "$CODECRACK_APP_PASSWORD" \
      --wait
    echo "Stapling notarization ticket ..."
    xcrun stapler staple "$APP"
    rm -f "$NOTARIZE_ZIP"
  else
    echo "Notarization skipped: set CODECRACK_APPLE_ID, CODECRACK_TEAM_ID, and " \
         "CODECRACK_APP_PASSWORD to notarize + staple."
  fi
else
  echo "Code signing skipped: set CODECRACK_SIGN_IDENTITY to sign (and, with the" \
       "notarization creds, notarize) the bundle."
fi

# CI / scripted builds set CODECRACK_SKIP_LAUNCH=1 to assemble the bundle without
# launching the GUI (there's no display, and it must return promptly).
if [ -n "${CODECRACK_SKIP_LAUNCH:-}" ]; then
  echo "CODECRACK_SKIP_LAUNCH set; not launching $APP."
  exit 0
fi

# `open` only activates an already-running instance; it won't swap in the new binary.
# Quit any running copy first so the freshly built one actually launches.
killall PPIDE 2>/dev/null || true
open "$APP"
