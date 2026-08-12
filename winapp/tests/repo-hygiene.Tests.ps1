BeforeAll {
    $root = Join-Path $PSScriptRoot '..\..'
    $script:Ignore = Get-Content (Join-Path $root '.gitignore')
    $script:Readme = Join-Path $root 'winapp\README.md'
}

Describe '.gitignore covers .NET build output' {
    It 'ignores bin/ obj/ *.user *.suo' {
        $script:Ignore | Should -Contain 'bin/'
        $script:Ignore | Should -Contain 'obj/'
        $script:Ignore | Should -Contain '*.user'
        $script:Ignore | Should -Contain '*.suo'
    }
    It 'still ignores the existing build/ and dist/ dirs' {
        $script:Ignore | Should -Contain 'build/'
        $script:Ignore | Should -Contain 'dist/'
    }
}

Describe 'winapp/README.md documents build + run' {
    It 'exists' { Test-Path $script:Readme | Should -BeTrue }
    It 'documents make-app.ps1 and the dist output' {
        $text = Get-Content $script:Readme -Raw
        $text | Should -Match 'make-app\.ps1'
        $text | Should -Match 'dist\\CodeCrack'
    }
}
