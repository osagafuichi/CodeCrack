BeforeAll {
    # Dot-source the script under test; its entry point is guarded so nothing runs on import.
    . (Join-Path $PSScriptRoot '..\fetch-python-runtime.ps1')
}

Describe 'fetch-python-runtime helpers' {
    It 'builds the exact pinned Windows x64 install_only asset name' {
        Get-PbsAsset '3.12.13' '20260623' |
            Should -BeExactly 'cpython-3.12.13+20260623-x86_64-pc-windows-msvc-install_only.tar.gz'
    }

    It 'builds the GitHub release download URL from the asset + release tag' {
        $asset = 'cpython-3.12.13+20260623-x86_64-pc-windows-msvc-install_only.tar.gz'
        Get-PbsUrl $asset '20260623' |
            Should -BeExactly "https://github.com/astral-sh/python-build-standalone/releases/download/20260623/$asset"
    }

    It 'exposes an Invoke-FetchRuntime entry point that is not run on dot-source' {
        Get-Command Invoke-FetchRuntime -CommandType Function | Should -Not -BeNullOrEmpty
    }

    It 'keeps the pinned versions in sync with the .sh sibling' {
        $sh = Get-Content (Join-Path $PSScriptRoot '..\fetch-python-runtime.sh') -Raw
        $sh | Should -Match 'PBS_RELEASE="20260623"'
        $sh | Should -Match 'PY_VERSION="3.12.13"'
    }
}
