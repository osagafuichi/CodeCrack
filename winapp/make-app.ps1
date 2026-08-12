#!/usr/bin/env pwsh
# Publish the CodeCrack WPF app as a self-contained single-file Windows x64 build and
# assemble the shippable dist\CodeCrack\ tree: the exe + bundled engine + embedded
# CPython/pytest. Mirrors macapp/make-app.sh.
#
# Code signing is OPTIONAL and guarded: it runs only when $env:CODECRACK_WINDOWS_CERT
# points at a .pfx (with $env:CODECRACK_WINDOWS_CERT_PASSWORD). Launch is suppressed
# when $env:CODECRACK_SKIP_LAUNCH is set (CI / headless).
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SignTool {
    # Returns signtool.exe's path, or $null when signing isn't configured.
    if (-not $env:CODECRACK_WINDOWS_CERT) { return $null }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($sdk) { return $sdk.FullName }
    return $null
}

function Copy-Engine {
    # Copy engine\codecrack + pyproject.toml into $Dest, then prune __pycache__.
    param([Parameter(Mandatory)][string]$EngineSrc,
          [Parameter(Mandatory)][string]$Dest)
    if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    Copy-Item -Recurse (Join-Path $EngineSrc 'codecrack') (Join-Path $Dest 'codecrack')
    Copy-Item (Join-Path $EngineSrc 'pyproject.toml') (Join-Path $Dest 'pyproject.toml')
    Get-ChildItem -Path $Dest -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
}

function Invoke-MakeApp {
    $winapp   = $PSScriptRoot
    $repoRoot = Split-Path -Parent $winapp                # winapp\ -> repo root
    $proj     = Join-Path $winapp 'CodeCrackApp\CodeCrackApp.csproj'
    $publishDir = Join-Path $winapp 'CodeCrackApp\bin\Release\net8.0-windows\win-x64\publish'
    $distDir  = Join-Path $repoRoot 'dist\CodeCrack'

    Write-Host 'Publishing (self-contained, single-file, WPF: no trimming) ...'
    dotnet publish $proj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    Copy-Item -Recurse (Join-Path $publishDir '*') $distDir

    Write-Host 'Bundling engine into Resources\engine ...'
    Copy-Engine (Join-Path $repoRoot 'engine') (Join-Path $distDir 'Resources\engine')

    Write-Host 'Preparing embedded Python runtime ...'
    & (Join-Path $repoRoot 'scripts\fetch-python-runtime.ps1')
    if ($LASTEXITCODE -ne 0) { throw "fetch-python-runtime.ps1 failed ($LASTEXITCODE)" }
    $pyRuntime = Join-Path $repoRoot 'build\python-runtime\python'
    $pyDest    = Join-Path $distDir 'python'
    if (Test-Path $pyDest) { Remove-Item -Recurse -Force $pyDest }
    Copy-Item -Recurse $pyRuntime $pyDest
    Get-ChildItem -Path $pyDest -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force

    Write-Host 'Bundling license notices ...'
    Copy-Item (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt') $distDir
    Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $distDir 'LICENSE.txt')

    Write-Host 'Pruning debug symbols (*.pdb) from the distributable ...'
    Get-ChildItem -Path $distDir -Recurse -Filter '*.pdb' -ErrorAction SilentlyContinue |
        Remove-Item -Force

    $exe = Join-Path $distDir 'CodeCrack.exe'
    $signtool = Get-SignTool
    if ($signtool) {
        Write-Host "Code signing $exe ..."
        & $signtool sign /fd SHA256 /f $env:CODECRACK_WINDOWS_CERT `
            /p $env:CODECRACK_WINDOWS_CERT_PASSWORD `
            /tr http://timestamp.digicert.com /td SHA256 $exe
        if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
    } else {
        Write-Host 'Code signing skipped: set CODECRACK_WINDOWS_CERT (+ CODECRACK_WINDOWS_CERT_PASSWORD) to sign.'
    }

    Write-Host "Built $distDir"

    if ($env:CODECRACK_SKIP_LAUNCH) {
        Write-Host 'CODECRACK_SKIP_LAUNCH set; not launching.'
        return
    }
    Start-Process $exe
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-MakeApp
}
