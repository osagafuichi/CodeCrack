#!/usr/bin/env pwsh
# Fetch an embeddable CPython (python-build-standalone) for Windows x64 and install
# pytest into it, producing a self-contained interpreter the Windows app bundles so
# users need no system Python. Mirrors scripts/fetch-python-runtime.sh — KEEP THE
# PINNED VERSIONS BELOW IN SYNC with that file.
#
# Output layout (relative to repo root):
#   build\python-runtime\python\python.exe                     <- the interpreter
#   build\python-runtime\python\Lib\site-packages\pytest\...   <- pytest
#
# Re-runnable and cached: tarball -> build\python-cache\, runtime -> build\python-runtime\,
# pytest install guarded by a stamp file. Delete build\python-runtime\ to force a rebuild.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Pinned versions (bump together with scripts/fetch-python-runtime.sh) ---
$script:PbsRelease = '20260623'
$script:PyVersion  = '3.12.13'
$script:PytestSpec = 'pytest>=8,<9'

function Get-PbsAsset {
    param([Parameter(Mandatory)][string]$PyVersion,
          [Parameter(Mandatory)][string]$PbsRelease)
    "cpython-$PyVersion+$PbsRelease-x86_64-pc-windows-msvc-install_only.tar.gz"
}

function Get-PbsUrl {
    param([Parameter(Mandatory)][string]$Asset,
          [Parameter(Mandatory)][string]$PbsRelease)
    "https://github.com/astral-sh/python-build-standalone/releases/download/$PbsRelease/$Asset"
}

function Invoke-FetchRuntime {
    $repoRoot   = Split-Path -Parent $PSScriptRoot       # scripts\ -> repo root
    $cacheDir   = Join-Path $repoRoot 'build\python-cache'
    $runtimeDir = Join-Path $repoRoot 'build\python-runtime'
    $pyHome     = Join-Path $runtimeDir 'python'
    $pythonBin  = Join-Path $pyHome 'python.exe'
    $stamp      = Join-Path $pyHome '.codecrack-pytest-installed'

    $asset   = Get-PbsAsset $script:PyVersion $script:PbsRelease
    $url     = Get-PbsUrl $asset $script:PbsRelease
    $tarball = Join-Path $cacheDir $asset

    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null

    if (-not (Test-Path $tarball)) {
        Write-Host "Downloading $asset ..."
        Invoke-WebRequest -Uri $url -OutFile "$tarball.tmp"
        Move-Item -Force "$tarball.tmp" $tarball
    } else {
        Write-Host "Using cached $asset"
    }

    if (-not (Test-Path $pythonBin)) {
        Write-Host "Extracting runtime into $runtimeDir ..."
        if (Test-Path $runtimeDir) { Remove-Item -Recurse -Force $runtimeDir }
        New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
        tar -xzf $tarball -C $runtimeDir   # unpacks a top-level python\ directory
        if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
    }

    if (-not (Test-Path $stamp)) {
        Write-Host "Installing $script:PytestSpec into the embedded runtime ..."
        & $pythonBin -m pip install --no-warn-script-location --upgrade pip
        if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed ($LASTEXITCODE)" }
        & $pythonBin -m pip install $script:PytestSpec
        if ($LASTEXITCODE -ne 0) { throw "pip install pytest failed ($LASTEXITCODE)" }
        & $pythonBin -c "import pytest; print('pytest', pytest.__version__, 'ready')"
        if ($LASTEXITCODE -ne 0) { throw "pytest import check failed ($LASTEXITCODE)" }
        New-Item -ItemType File -Path $stamp | Out-Null
    } else {
        Write-Host "pytest already installed in the embedded runtime"
    }

    Write-Host "Embedded runtime ready: $pythonBin"
    $pythonBin
}

# Run the entry point only on direct execution. Pester dot-sources this file
# (`. $path`), where InvocationName is '.', so tests import the functions without
# triggering a network download.
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-FetchRuntime
}
