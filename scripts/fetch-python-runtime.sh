#!/bin/bash
# Fetch an embeddable CPython (python-build-standalone) and install pytest into it,
# producing a self-contained interpreter the macOS app can bundle so friends don't
# need a system python3.
#
# Re-runnable and cached: the tarball is downloaded once into build/python-cache/,
# the runtime is extracted once into build/python-runtime/, and pytest is installed
# once (a stamp file guards re-installs). Delete build/python-runtime/ to force a
# clean rebuild.
#
# make-app.sh calls this, then copies build/python-runtime/python into the .app's
# Resources. It can also be run standalone to refresh the local runtime.
#
# Output layout (relative to repo root):
#   build/python-runtime/python/bin/python3        <- the interpreter
#   build/python-runtime/python/lib/pythonX.Y/site-packages/pytest/...
#
# Everything lives under build/ (gitignored), so the ~50 MB runtime is never
# snapshotted into version control.
set -euo pipefail

# --- Pinned versions (bump together; keep PY_VERSION's minor matching the app) ---
PBS_RELEASE="20260623"
PY_VERSION="3.12.13"
# pytest is REQUIRED: Milestone 1's execute stage shells out to `python -m pytest`
# using sys.executable, i.e. this very interpreter, so pytest must be importable here.
PYTEST_SPEC="pytest>=8,<9"

# --- Paths ---
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CACHE_DIR="$REPO_ROOT/build/python-cache"
RUNTIME_DIR="$REPO_ROOT/build/python-runtime"
PY_HOME="$RUNTIME_DIR/python"
PYTHON_BIN="$PY_HOME/bin/python3"
STAMP="$PY_HOME/.codecrack-pytest-installed"

# --- Resolve the python-build-standalone asset for this Mac's architecture ---
case "$(uname -m)" in
  arm64|aarch64) PBS_ARCH="aarch64" ;;
  x86_64)        PBS_ARCH="x86_64" ;;
  *) echo "error: unsupported architecture $(uname -m)" >&2; exit 1 ;;
esac
ASSET="cpython-${PY_VERSION}+${PBS_RELEASE}-${PBS_ARCH}-apple-darwin-install_only.tar.gz"
URL="https://github.com/astral-sh/python-build-standalone/releases/download/${PBS_RELEASE}/${ASSET}"
TARBALL="$CACHE_DIR/$ASSET"

mkdir -p "$CACHE_DIR"

# --- 1. Download (cached) ---
if [ ! -f "$TARBALL" ]; then
  echo "Downloading $ASSET ..."
  curl -fL --retry 3 -o "$TARBALL.tmp" "$URL"
  mv "$TARBALL.tmp" "$TARBALL"
else
  echo "Using cached $ASSET"
fi

# --- 2. Extract (once) ---
if [ ! -x "$PYTHON_BIN" ]; then
  echo "Extracting runtime into $RUNTIME_DIR ..."
  rm -rf "$RUNTIME_DIR"
  mkdir -p "$RUNTIME_DIR"
  tar -xzf "$TARBALL" -C "$RUNTIME_DIR"   # unpacks a top-level python/ directory
fi

# --- 3. Install pytest into the embedded runtime (once) ---
if [ ! -f "$STAMP" ]; then
  echo "Installing $PYTEST_SPEC into the embedded runtime ..."
  "$PYTHON_BIN" -m pip install --no-warn-script-location --upgrade pip
  "$PYTHON_BIN" -m pip install "$PYTEST_SPEC"
  "$PYTHON_BIN" -c "import pytest; print('pytest', pytest.__version__, 'ready')"
  touch "$STAMP"
else
  echo "pytest already installed in the embedded runtime"
fi

echo "Embedded runtime ready: $PYTHON_BIN"
