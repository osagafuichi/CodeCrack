BeforeAll {
    . (Join-Path $PSScriptRoot '..\make-app.ps1')
}

Describe 'make-app helpers' {
    It 'Get-SignTool returns null when CODECRACK_WINDOWS_CERT is unset' {
        $saved = $env:CODECRACK_WINDOWS_CERT
        Remove-Item Env:CODECRACK_WINDOWS_CERT -ErrorAction SilentlyContinue
        try { Get-SignTool | Should -BeNullOrEmpty }
        finally { if ($null -ne $saved) { $env:CODECRACK_WINDOWS_CERT = $saved } }
    }

    It 'Copy-Engine copies the package + pyproject and prunes __pycache__' {
        $work = Join-Path ([System.IO.Path]::GetTempPath()) ("cc-eng-" + [guid]::NewGuid())
        $src  = Join-Path $work 'engine'
        $dst  = Join-Path $work 'out\Resources\engine'
        New-Item -ItemType Directory -Force -Path (Join-Path $src 'codecrack\__pycache__') | Out-Null
        Set-Content (Join-Path $src 'codecrack\__main__.py')       '# entry'
        Set-Content (Join-Path $src 'codecrack\__pycache__\x.pyc') 'junk'
        Set-Content (Join-Path $src 'pyproject.toml')              '[project]'
        try {
            Copy-Engine $src $dst
            Test-Path (Join-Path $dst 'codecrack\__main__.py') | Should -BeTrue
            Test-Path (Join-Path $dst 'pyproject.toml')        | Should -BeTrue
            Test-Path (Join-Path $dst 'codecrack\__pycache__') | Should -BeFalse
        }
        finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
    }

    It 'exposes an Invoke-MakeApp entry point that is not run on dot-source' {
        Get-Command Invoke-MakeApp -CommandType Function | Should -Not -BeNullOrEmpty
    }
}
