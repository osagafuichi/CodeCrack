#!/usr/bin/env pwsh
# Publish the CodeCrack WPF app as a self-contained single-file Windows x64 build and
# assemble the shippable dist\CodeCrack\ tree: the exe + bundled engine + embedded
# CPython/pytest. Mirrors macapp/make-app.sh.
#
# Versioning: pass a version via $env:CODECRACK_VERSION (e.g. from a git tag in CI);
# it flows into the exe's file/product version. Defaults to 1.0.0 (Directory.Build.props).
#
# Code signing is OPTIONAL and fully credential-guarded -- a no-op with no creds, a
# clean signature WITH them. Two mutually-exclusive backends are supported (see
# docs/SIGNING.md):
#   (a) Azure Trusted Signing  -- CODECRACK_AZURE_ENDPOINT + _ACCOUNT + _CERT_PROFILE
#       (recommended: no HSM, fits GitHub Actions). Signs via signtool + the
#       Azure.CodeSigning dlib; Azure auth comes from the ambient credential
#       (AZURE_TENANT_ID/_CLIENT_ID/_CLIENT_SECRET or an azure/login OIDC session).
#   (b) PFX certificate        -- CODECRACK_WINDOWS_CERT (path to .pfx) + _PASSWORD.
# Either way the signature is re-checked with `signtool verify /pa` before we finish.
#
# Launch is suppressed when $env:CODECRACK_SKIP_LAUNCH is set (CI / headless).
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SignTool {
    # Resolve signtool.exe robustly: PATH first, then the newest Windows SDK bin
    # (x64, then x86). Returns $null when signtool isn't installed. This is
    # independent of any signing credentials -- both signing backends and the
    # post-sign `verify` reuse it.
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($arch in 'x64', 'x86', 'arm64') {
        $hit = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\$arch\signtool.exe" `
                    -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Resolve-SigningMode {
    # Which signing backend is configured, purely from env. Azure wins if both are
    # set. 'azure' | 'pfx' | 'none'. 'none' => signing is a graceful no-op.
    if ($env:CODECRACK_AZURE_ENDPOINT -and $env:CODECRACK_AZURE_ACCOUNT -and $env:CODECRACK_AZURE_CERT_PROFILE) {
        return 'azure'
    }
    if ($env:CODECRACK_WINDOWS_CERT) { return 'pfx' }
    return 'none'
}

function Resolve-AzureDlib {
    # Locate Azure.CodeSigning.Dlib.dll for Trusted Signing. Explicit
    # $env:CODECRACK_AZURE_DLIB wins; otherwise search the NuGet global-packages
    # cache for the Microsoft.Trusted.Signing.Client package (what the
    # azure/trusted-signing-action restores). Throws with guidance if not found.
    if ($env:CODECRACK_AZURE_DLIB) {
        if (-not (Test-Path $env:CODECRACK_AZURE_DLIB)) {
            throw "CODECRACK_AZURE_DLIB is set but the file does not exist: $env:CODECRACK_AZURE_DLIB"
        }
        return $env:CODECRACK_AZURE_DLIB
    }
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
                 else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $pkg = Join-Path $nugetRoot 'microsoft.trusted.signing.client'
    $hit = Get-ChildItem -Path $pkg -Recurse -Filter 'Azure.CodeSigning.Dlib.dll' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    throw 'Azure Trusted Signing dlib not found. Point CODECRACK_AZURE_DLIB at ' +
          'Azure.CodeSigning.Dlib.dll, or install the Microsoft.Trusted.Signing.Client ' +
          'NuGet package (the azure/trusted-signing-action does this). See docs/SIGNING.md.'
}

function Invoke-Sign {
    # Sign $Exe with whichever backend env configures, then verify. No-op when
    # nothing is configured, so a credential-less build still succeeds.
    param([Parameter(Mandatory)][string]$Exe)

    $mode = Resolve-SigningMode
    if ($mode -eq 'none') {
        Write-Host ('Code signing skipped (no credentials). Set CODECRACK_AZURE_ENDPOINT/_ACCOUNT/_CERT_PROFILE ' +
                    'for Azure Trusted Signing, or CODECRACK_WINDOWS_CERT (+ _PASSWORD) for a PFX. See docs/SIGNING.md.')
        return
    }

    $signtool = Get-SignTool
    if (-not $signtool) {
        throw 'Signing was requested but signtool.exe could not be found. Install the Windows SDK ' +
              '(App Certification Kit component) or add signtool.exe to PATH.'
    }

    if ($mode -eq 'azure') {
        Write-Host "Code signing (Azure Trusted Signing) $Exe ..."
        $dlib = Resolve-AzureDlib
        $meta = [System.IO.Path]::GetTempFileName()
        @{
            Endpoint               = $env:CODECRACK_AZURE_ENDPOINT
            CodeSigningAccountName = $env:CODECRACK_AZURE_ACCOUNT
            CertificateProfileName = $env:CODECRACK_AZURE_CERT_PROFILE
        } | ConvertTo-Json | Set-Content -Encoding utf8 -Path $meta
        try {
            & $signtool sign /v /fd SHA256 `
                /tr 'http://timestamp.acs.microsoft.com' /td SHA256 `
                /dlib $dlib /dmdf $meta $Exe
            if ($LASTEXITCODE -ne 0) { throw "signtool (Azure Trusted Signing) failed ($LASTEXITCODE)" }
        }
        finally { Remove-Item $meta -Force -ErrorAction SilentlyContinue }
    }
    else {   # pfx
        Write-Host "Code signing (PFX certificate) $Exe ..."
        & $signtool sign /fd SHA256 /f $env:CODECRACK_WINDOWS_CERT `
            /p $env:CODECRACK_WINDOWS_CERT_PASSWORD `
            /tr http://timestamp.digicert.com /td SHA256 $Exe
        if ($LASTEXITCODE -ne 0) { throw "signtool (PFX) failed ($LASTEXITCODE)" }
    }

    Write-Host 'Verifying signature (signtool verify /pa) ...'
    & $signtool verify /pa $Exe
    if ($LASTEXITCODE -ne 0) { throw "signtool verify /pa failed ($LASTEXITCODE)" }
    Write-Host 'Signature verified.'
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

    # Version override (git tag in CI). Empty => Directory.Build.props default (1.0.0).
    $versionArgs = @()
    if ($env:CODECRACK_VERSION) {
        Write-Host "Stamping version $env:CODECRACK_VERSION"
        $versionArgs = @("-p:Version=$env:CODECRACK_VERSION")
    }

    Write-Host 'Publishing (self-contained, single-file, WPF: no trimming) ...'
    dotnet publish $proj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true @versionArgs
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
    Invoke-Sign -Exe $exe

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
