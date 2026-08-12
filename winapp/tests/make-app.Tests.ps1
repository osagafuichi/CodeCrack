BeforeAll {
    . (Join-Path $PSScriptRoot '..\make-app.ps1')

    # Snapshot + clear every signing-related env var so Resolve-SigningMode sees a
    # clean slate regardless of the host environment; restored in AfterAll.
    $script:SignEnvNames = @(
        'CODECRACK_AZURE_ENDPOINT', 'CODECRACK_AZURE_ACCOUNT', 'CODECRACK_AZURE_CERT_PROFILE',
        'CODECRACK_WINDOWS_CERT', 'CODECRACK_WINDOWS_CERT_PASSWORD'
    )
    $script:SavedSignEnv = @{}
    foreach ($n in $script:SignEnvNames) {
        $script:SavedSignEnv[$n] = [Environment]::GetEnvironmentVariable($n)
        Remove-Item "Env:$n" -ErrorAction SilentlyContinue
    }
}

AfterAll {
    foreach ($n in $script:SignEnvNames) {
        if ($null -ne $script:SavedSignEnv[$n]) { Set-Item "Env:$n" $script:SavedSignEnv[$n] }
        else { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
    }
}

Describe 'make-app helpers' {
    Context 'Resolve-SigningMode' {
        BeforeEach {
            foreach ($n in $script:SignEnvNames) { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
        }

        It 'is a no-op (none) when no signing env is set' {
            Resolve-SigningMode | Should -Be 'none'
        }

        It 'selects pfx when CODECRACK_WINDOWS_CERT is set' {
            $env:CODECRACK_WINDOWS_CERT = 'C:\cert.pfx'
            Resolve-SigningMode | Should -Be 'pfx'
        }

        It 'selects azure when the three Trusted Signing vars are set' {
            $env:CODECRACK_AZURE_ENDPOINT     = 'https://eus.codesigning.azure.net'
            $env:CODECRACK_AZURE_ACCOUNT      = 'cc-signing'
            $env:CODECRACK_AZURE_CERT_PROFILE = 'cc-profile'
            Resolve-SigningMode | Should -Be 'azure'
        }

        It 'prefers azure over pfx when both are configured' {
            $env:CODECRACK_WINDOWS_CERT       = 'C:\cert.pfx'
            $env:CODECRACK_AZURE_ENDPOINT     = 'https://eus.codesigning.azure.net'
            $env:CODECRACK_AZURE_ACCOUNT      = 'cc-signing'
            $env:CODECRACK_AZURE_CERT_PROFILE = 'cc-profile'
            Resolve-SigningMode | Should -Be 'azure'
        }

        It 'does not select azure when only some Trusted Signing vars are set' {
            $env:CODECRACK_AZURE_ENDPOINT = 'https://eus.codesigning.azure.net'
            $env:CODECRACK_AZURE_ACCOUNT  = 'cc-signing'
            # CERT_PROFILE deliberately missing
            Resolve-SigningMode | Should -Be 'none'
        }
    }

    It 'Invoke-Sign is a graceful no-op when nothing is configured' {
        foreach ($n in $script:SignEnvNames) { Remove-Item "Env:$n" -ErrorAction SilentlyContinue }
        # Passing a path that does not exist must NOT throw: with no creds it returns early.
        { Invoke-Sign -Exe 'C:\does\not\exist.exe' } | Should -Not -Throw
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
