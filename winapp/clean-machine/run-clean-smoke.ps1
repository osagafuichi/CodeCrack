#requires -Version 5
<#
.SYNOPSIS
  Clean-room smoke test for the packaged CodeCrack Windows bundle.

.DESCRIPTION
  Run this INSIDE a pristine machine (Windows Sandbox or a fresh VM with no Python
  and no .NET installed) to prove the self-contained download actually works for an
  end user. It:
    1. reports whether system Python/.NET are present (they should NOT be);
    2. runs the BUNDLED python + engine to analyze a seeded bug — asserting it
       reproduces with no system Python;
    3. launches CodeCrack.exe and confirms the GUI stays up, then closes it.

  Place this script next to CodeCrack.exe (copy it into the unzipped
  CodeCrack-windows folder) and run it, or point CodeCrack.wsb at that folder.
#>
[CmdletBinding()]
param([string]$DistPath = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$exe    = Join-Path $DistPath 'CodeCrack.exe'
$py     = Join-Path $DistPath 'python\python.exe'
$engine = Join-Path $DistPath 'Resources\engine'
if (-not (Test-Path $exe)) { throw "CodeCrack.exe not found in '$DistPath'. Copy this script next to it." }

Write-Host '== environment (this should be a pristine box) ==' -ForegroundColor Cyan
foreach ($tool in 'python', 'python3', 'dotnet') {
    $cmd = Get-Command $tool -ErrorAction SilentlyContinue
    if ($cmd) { Write-Host ("  {0}: PRESENT ({1}) - NOT a clean box" -f $tool, $cmd.Source) -ForegroundColor Yellow }
    else      { Write-Host ("  {0}: absent (good)" -f $tool) }
}

# 1) Bundled engine + Python analyze a seeded bug with NO system Python.
Write-Host "`n== bundled analyze (no system Python) ==" -ForegroundColor Cyan
$fixture = Join-Path $env:TEMP 'cc_clean_bug.py'
"def divide(a, b):`n    return a / b`n" | Set-Content -Encoding utf8 -Path $fixture
Push-Location $engine
try { $out = & $py -m codecrack analyze $fixture --json } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "bundled analyze exited $LASTEXITCODE" }
$data = $out | ConvertFrom-Json
$reproduced = [int]$data.summary.reproduced
Write-Host ("  findings={0}  reproduced={1}" -f $data.summary.findings, $reproduced)
if ($reproduced -lt 1) { throw "clean-room analyze did not reproduce a bug (reproduced=$reproduced)" }

# 2) The GUI launches and stays up.
Write-Host "`n== GUI launch ==" -ForegroundColor Cyan
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 20
$alive = Get-Process -Name 'CodeCrack' -ErrorAction SilentlyContinue
if (-not $alive) { throw 'CodeCrack.exe did not stay running after launch' }
Write-Host ("  CodeCrack.exe launched and is running (PID {0})" -f ($alive.Id -join ', '))
$alive | Stop-Process -Force

Write-Host "`nCLEAN-ROOM SMOKE: PASS" -ForegroundColor Green
