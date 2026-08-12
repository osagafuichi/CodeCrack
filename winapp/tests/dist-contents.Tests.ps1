# Asserts the PRODUCED dist\CodeCrack\ tree is complete and clean. Run after
# make-app.ps1 (CI wires this into windows-build and release before zipping) so a
# bundle missing license notices, missing runtime pieces, or leaking *.pdb fails CI.
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)  # winapp\tests -> repo root
    $script:Dist     = Join-Path $script:RepoRoot 'dist\CodeCrack'
}

Describe 'Distributable bundle contents' {
    It 'dist\CodeCrack exists (build it with make-app.ps1 first)' {
        Test-Path $script:Dist | Should -BeTrue
    }
    It 'contains the app executable' {
        Test-Path (Join-Path $script:Dist 'CodeCrack.exe') | Should -BeTrue
    }
    It 'contains the bundled engine entry point' {
        Test-Path (Join-Path $script:Dist 'Resources\engine\codecrack\__main__.py') | Should -BeTrue
    }
    It 'contains the embedded Python interpreter' {
        Test-Path (Join-Path $script:Dist 'python\python.exe') | Should -BeTrue
    }
    It 'ships third-party license notices' {
        Test-Path (Join-Path $script:Dist 'THIRD-PARTY-NOTICES.txt') | Should -BeTrue
    }
    It 'ships a first-party LICENSE' {
        Test-Path (Join-Path $script:Dist 'LICENSE.txt') | Should -BeTrue
    }
    It 'ships the embedded Python license file (CPython + native-lib notices)' {
        Test-Path (Join-Path $script:Dist 'python\LICENSE.txt') | Should -BeTrue
    }
    It 'contains NO debug symbols (*.pdb)' {
        @(Get-ChildItem -Path $script:Dist -Recurse -Filter '*.pdb' -ErrorAction SilentlyContinue).Count |
            Should -Be 0
    }
}
