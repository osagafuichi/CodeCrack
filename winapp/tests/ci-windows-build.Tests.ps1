BeforeAll {
    $script:Ci = Get-Content (Join-Path $PSScriptRoot '..\..\.github\workflows\ci.yml') -Raw
}

Describe 'ci.yml windows-build job' {
    It 'declares a windows-build job on windows-latest' {
        $script:Ci | Should -Match '(?m)^\s{2}windows-build:'
        $script:Ci | Should -Match 'runs-on:\s*windows-latest'
    }
    It 'pins .NET 8.0.x via setup-dotnet' {
        $script:Ci | Should -Match 'actions/setup-dotnet@v4'
        $script:Ci | Should -Match "dotnet-version:\s*['""]?8\.0\.x"
    }
    It 'runs make-app.ps1 headless via CODECRACK_SKIP_LAUNCH' {
        $script:Ci | Should -Match 'CODECRACK_SKIP_LAUNCH:\s*["'']?1'
        $script:Ci | Should -Match 'make-app\.ps1'
    }
    It 'zips dist\CodeCrack into CodeCrack-windows.zip' {
        $script:Ci | Should -Match 'Compress-Archive'
        $script:Ci | Should -Match 'CodeCrack-windows\.zip'
    }
    It 'uploads the artifact and fails when no files are produced' {
        $script:Ci | Should -Match 'actions/upload-artifact@v4'
        $script:Ci | Should -Match 'if-no-files-found:\s*error'
    }
}
