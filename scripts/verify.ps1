[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root 'KeyboardDebounce.csproj'
$testProject = Join-Path $root 'tests\KeyboardDebounce.Tests\KeyboardDebounce.Tests.csproj'
$nugetConfig = Join-Path $root 'NuGet.Config'
$publishContractTests = Join-Path $root 'tests\PublishScript.Tests.ps1'

function Invoke-DotNet {
    & dotnet @args
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "dotnet $($args -join ' ') failed with exit code $exitCode."
    }
}

function Assert-StaticDependencyContracts {
    param(
        [string] $TestProjectPath,
        [string[]] $ExcludedTestFiles
    )

    $testDirectory = Split-Path -Parent $TestProjectPath
    $forbiddenPatterns = @(
        'Microsoft\\.UI\\.Xaml',
        'KeyboardDebounce\\.WinUI'
    )

    foreach ($file in Get-ChildItem -Path $testDirectory -Filter '*.cs' -File) {
        if ($ExcludedTestFiles -contains $file.Name) {
            continue
        }

        $source = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($pattern in $forbiddenPatterns) {
            if ($source -match $pattern) {
                throw "Forbidden test contract detected in $($file.Name): $pattern"
            }
        }
    }
}

Invoke-DotNet restore $project --configfile $nugetConfig
Invoke-DotNet restore $testProject --configfile $nugetConfig
Invoke-DotNet build $project -c Release --no-restore
Assert-StaticDependencyContracts -TestProjectPath $testProject -ExcludedTestFiles @(
    'DpiTestBootstrap.cs',
    'SettingsFormSmokeTests.cs',
    'SettingsFormUiTests.cs'
)
Invoke-DotNet test $testProject -c Release --no-restore

& $publishContractTests
if (-not $?) {
    throw "Publish script contract tests failed: $publishContractTests"
}

Write-Host 'Verification completed successfully.'
