$ErrorActionPreference = 'Stop'

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        $Expected,

        [Parameter(Mandatory = $true)]
        $Actual,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected: '$Expected'. Actual: '$Actual'."
    }
}

function Get-FileSnapshots {
    param([string[]] $Paths)

    $snapshots = @{}
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Cannot snapshot missing fixture file: $path"
        }
        $snapshots[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    return $snapshots
}

function Assert-FileSnapshotsUnchanged {
    param(
        [hashtable] $Snapshots,
        [string] $Message
    )

    foreach ($path in $Snapshots.Keys) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$Message Missing file: $path"
        }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actualHash -cne $Snapshots[$path]) {
            throw "$Message File changed: $path"
        }
    }
}

function Assert-NoReleaseTransactionFiles {
    param([string] $ReleaseDirectory)

    $transactionFiles = @(
        Get-ChildItem -LiteralPath $ReleaseDirectory -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '\.(pending|backup)$' }
    )
    if ($transactionFiles.Count -ne 0) {
        throw "Release transaction files were not cleaned up: $($transactionFiles.FullName -join ', ')"
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishScriptPath = Join-Path $repositoryRoot 'scripts\publish.ps1'
$verifyScriptPath = Join-Path $repositoryRoot 'scripts\verify.ps1'
$workflowPath = Join-Path $repositoryRoot '.github\workflows\windows-ci.yml'
$realDotNet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$projectXml = [xml](Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'KeyboardDebounce.csproj'))
Assert-Equal '0.2.0' ([string] $projectXml.Project.PropertyGroup.Version) 'KeyboardDebounce.csproj must declare the release version.'
Write-Host 'PASS: the project declares release version 0.2.0.'
Assert-Equal 'false' ([string] $projectXml.Project.PropertyGroup.EnableDefaultPageItems) 'WinUI default Page glob must stay disabled so ignored artifact XAML cannot enter the application resource graph.'
Write-Host 'PASS: WinUI compiles only explicitly declared XAML pages.'

$readme = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'README.md')
if ($readme -match 'Ctrl\+Alt\+F12' -or [regex]::Matches($readme, 'Ctrl\+Alt\+F11').Count -ne 2) {
    throw 'README must document Ctrl+Alt+F11 as the pause hotkey in both languages.'
}
Write-Host 'PASS: README documents Ctrl+Alt+F11.'

$publishSource = Get-Content -Raw -LiteralPath $publishScriptPath
if ($publishSource -match '(?i)\bStop-Process\b|\btaskkill(?:\.exe)?\b') {
    throw 'The publish script must never terminate a process to replace a release artifact.'
}
Write-Host 'PASS: publish never terminates processes holding release files.'

$verifySource = Get-Content -Raw -LiteralPath $verifyScriptPath
if ($verifySource -notmatch 'Invoke-DotNet\s+build' -or
    $verifySource -notmatch 'Invoke-DotNet\s+test' -or
    -not $verifySource.Contains('& $publishContractTests')) {
    throw 'verify.ps1 must run the .NET build/test suite and directly execute PublishScript.Tests.ps1.'
}
if ($verifySource -match '(?i)scripts[\\/]publish\.ps1' -or $verifySource -match '(?im)^\s*&\s+.*verify\.ps1') {
    throw 'verify.ps1 must not recursively invoke publish.ps1 or itself.'
}
$workflowSource = Get-Content -Raw -LiteralPath $workflowPath
if ($workflowSource -notmatch 'windows-latest' -or $workflowSource -notmatch '(?m)run:\s+\.\\scripts\\verify\.ps1\s*$') {
    throw 'The Windows GitHub Actions workflow must invoke scripts\verify.ps1.'
}
Write-Host 'PASS: unified verification and Windows CI cover both test suites without recursion.'

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("KeyboardDebounce-publish-test-" + [Guid]::NewGuid().ToString('N'))
$fixtureScriptDirectory = Join-Path $fixtureRoot 'scripts'
$fixtureTestProjectDirectory = Join-Path $fixtureRoot 'tests\KeyboardDebounce.Tests'
$fakeBin = Join-Path $fixtureRoot 'fake-bin'
$stubProjectDirectory = Join-Path $fixtureRoot 'stub-app'
$stubOutputDirectory = Join-Path $stubProjectDirectory 'output'
$fakeDotNetLog = Join-Path $fixtureRoot 'fake-dotnet.log'
$originalPath = $env:PATH
$testEnvironmentVariableNames = @(
    'FAKE_DOTNET_LOG',
    'FAKE_DOTNET_EXIT_CODE',
    'FAKE_DOTNET_FAIL_COMMAND',
    'FAKE_DOTNET_PUBLISH_SIDECAR',
    'FAKE_PUBLISHED_EXE',
    'VERIFY_CONTRACT_LOG'
)
$originalTestEnvironment = @{}
foreach ($name in $testEnvironmentVariableNames) {
    $originalTestEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$publishEnvironmentVariableNames = @(
    'DOTNET_CLI_HOME',
    'NUGET_PACKAGES',
    'NUGET_HTTP_CACHE_PATH',
    'APPDATA',
    'LOCALAPPDATA',
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'DOTNET_CLI_TELEMETRY_OPTOUT'
)
$originalPublishEnvironment = @{}
foreach ($name in $publishEnvironmentVariableNames) {
    $originalPublishEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    New-Item -ItemType Directory -Path $fixtureScriptDirectory, $fixtureTestProjectDirectory, $fakeBin, $stubProjectDirectory -Force | Out-Null
    Copy-Item -LiteralPath $publishScriptPath -Destination (Join-Path $fixtureScriptDirectory 'publish.ps1')
    Copy-Item -LiteralPath $verifyScriptPath -Destination (Join-Path $fixtureScriptDirectory 'verify.ps1')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'KeyboardDebounce.csproj') -Destination (Join-Path $fixtureRoot 'KeyboardDebounce.csproj')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'tests\KeyboardDebounce.Tests\KeyboardDebounce.Tests.csproj') -Destination (Join-Path $fixtureTestProjectDirectory 'KeyboardDebounce.Tests.csproj')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'NuGet.Config') -Destination (Join-Path $fixtureRoot 'NuGet.Config')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination (Join-Path $fixtureRoot 'README.md')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $fixtureRoot 'LICENSE')
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'assets') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'assets\keyboard-debounce.ico') -Destination (Join-Path $fixtureRoot 'assets\keyboard-debounce.ico')

    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>KeyboardDebounce</AssemblyName>
    <Version>9.8.7</Version>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $stubProjectDirectory 'Stub.csproj') -Encoding UTF8
    'internal static class Program { private static void Main() { } }' | Set-Content -LiteralPath (Join-Path $stubProjectDirectory 'Program.cs') -Encoding UTF8
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
  </packageSources>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $stubProjectDirectory 'NuGet.Config') -Encoding UTF8

    & $realDotNet restore (Join-Path $stubProjectDirectory 'Stub.csproj') --configfile (Join-Path $stubProjectDirectory 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restore the isolated FileVersion test fixture; dotnet exited with $LASTEXITCODE."
    }
    & $realDotNet build (Join-Path $stubProjectDirectory 'Stub.csproj') -c Release --no-restore -o $stubOutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Could not build the isolated FileVersion test fixture; dotnet exited with $LASTEXITCODE."
    }
    $fakePublishedExe = Join-Path $stubOutputDirectory 'KeyboardDebounce.exe'
    Assert-Equal '9.8.7.0' (Get-Item -LiteralPath $fakePublishedExe).VersionInfo.FileVersion 'The isolated EXE fixture must carry FileVersion 9.8.7.0.'

@'
$DotNetArguments = @(Get-Content -LiteralPath ($env:FAKE_DOTNET_LOG + '.args'))
$command = if ($DotNetArguments.Count -gt 0) { $DotNetArguments[0] } else { '' }
if ($env:FAKE_DOTNET_FAIL_COMMAND -eq $command) {
    exit 42
}
if ($env:FAKE_DOTNET_EXIT_CODE -ne '0') {
    exit [int] $env:FAKE_DOTNET_EXIT_CODE
}
if ($command -ne 'publish') {
    exit 0
}

$assemblyNamePrefix = '-p:AssemblyName='
$assemblyName = $null
for ($index = 0; $index -lt $DotNetArguments.Count; $index++) {
    $argument = $DotNetArguments[$index]
    if ($argument.StartsWith($assemblyNamePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $assemblyName = $argument.Substring($assemblyNamePrefix.Length)
        break
    }
    if ($argument.Equals('-p:AssemblyName', [StringComparison]::OrdinalIgnoreCase) -and
        $index + 1 -lt $DotNetArguments.Count) {
        $assemblyName = $DotNetArguments[$index + 1]
        break
    }
}
$outputIndex = [Array]::IndexOf($DotNetArguments, '-o')
if ([string]::IsNullOrWhiteSpace($assemblyName) -or $outputIndex -lt 0 -or $outputIndex + 1 -ge $DotNetArguments.Count) {
    [Console]::Error.WriteLine("Malformed fake dotnet publish arguments: $($DotNetArguments -join ' | ')")
    exit 43
}

$publishOutput = $DotNetArguments[$outputIndex + 1]
Copy-Item -LiteralPath $env:FAKE_PUBLISHED_EXE -Destination (Join-Path $publishOutput "$assemblyName.exe") -Force
if (-not [string]::IsNullOrEmpty($env:FAKE_DOTNET_PUBLISH_SIDECAR)) {
    Set-Content -LiteralPath (Join-Path $publishOutput 'KeyboardDebounce.Sidecar.dll') -Value $env:FAKE_DOTNET_PUBLISH_SIDECAR -Encoding Ascii
}
'@ | Set-Content -LiteralPath (Join-Path $fakeBin 'fake-dotnet.ps1') -Encoding UTF8
@'
@echo off
echo %*>> "%FAKE_DOTNET_LOG%"
> "%FAKE_DOTNET_LOG%.args" (
  for %%A in (%*) do @echo %%~A
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-dotnet.ps1"
exit /b %ERRORLEVEL%
'@ | Set-Content -LiteralPath (Join-Path $fakeBin 'dotnet.cmd') -Encoding Ascii

    $env:FAKE_DOTNET_LOG = $fakeDotNetLog
    $env:FAKE_DOTNET_EXIT_CODE = '42'
    $env:PATH = "$fakeBin;$originalPath"
    foreach ($name in $publishEnvironmentVariableNames) {
        [Environment]::SetEnvironmentVariable($name, "sentinel-$name", 'Process')
    }

    $caught = $null
    try {
        & (Join-Path $fixtureScriptDirectory 'publish.ps1')
    }
    catch {
        $caught = $_
    }

    if ($null -eq $caught) {
        throw 'The publish script did not fail when dotnet restore returned exit code 42.'
    }
    $dotNetCalls = @(Get-Content -LiteralPath $fakeDotNetLog)
    Assert-Equal 1 $dotNetCalls.Count 'The publish script must stop after the first failed dotnet command.'
    Write-Host 'PASS: publish stops after the first failed dotnet command.'

    foreach ($name in $publishEnvironmentVariableNames) {
        $actualValue = [Environment]::GetEnvironmentVariable($name, 'Process')
        Assert-Equal "sentinel-$name" $actualValue "The publish script must restore $name after a failure."
    }
    Write-Host 'PASS: publish restores its environment variables after a failure.'

    $failureCases = @(
        @{ Command = 'build'; ExpectedCalls = 3 },
        @{ Command = 'test'; ExpectedCalls = 4 },
        @{ Command = 'publish'; ExpectedCalls = 5 }
    )
    $env:FAKE_DOTNET_EXIT_CODE = '0'
    foreach ($failureCase in $failureCases) {
        Clear-Content -LiteralPath $fakeDotNetLog
        $env:FAKE_DOTNET_FAIL_COMMAND = $failureCase.Command
        $commandError = $null
        try {
            & (Join-Path $fixtureScriptDirectory 'publish.ps1')
        }
        catch {
            $commandError = $_
        }
        if ($null -eq $commandError -or $commandError.Exception.Message -notmatch 'exit code 42') {
            throw "The publish script must surface a nonzero dotnet $($failureCase.Command) exit code."
        }
        Assert-Equal $failureCase.ExpectedCalls @(Get-Content -LiteralPath $fakeDotNetLog).Count "The publish script must stop immediately after dotnet $($failureCase.Command) fails."
    }
    Write-Host 'PASS: every dotnet build/test/publish failure stops the release immediately.'

    $fixtureProjectPath = Join-Path $fixtureRoot 'KeyboardDebounce.csproj'
    $fixtureProjectXml = [xml](Get-Content -Raw -LiteralPath $fixtureProjectPath)
    $fixtureProjectXml.Project.PropertyGroup.Version = '9.8.7'
    $fixtureProjectXml.Save($fixtureProjectPath)
    Clear-Content -LiteralPath $fakeDotNetLog
    $env:FAKE_DOTNET_EXIT_CODE = '0'
    $env:FAKE_DOTNET_FAIL_COMMAND = ''
    $env:FAKE_PUBLISHED_EXE = $fakePublishedExe

    & (Join-Path $fixtureScriptDirectory 'publish.ps1')

    foreach ($name in $publishEnvironmentVariableNames) {
        $actualValue = [Environment]::GetEnvironmentVariable($name, 'Process')
        Assert-Equal "sentinel-$name" $actualValue "The publish script must restore $name after success."
    }
    Write-Host 'PASS: publish restores its environment variables after success.'

    $fixtureReleaseDirectory = Join-Path $fixtureRoot 'releases'
    $versionedReleaseExe = Join-Path $fixtureReleaseDirectory 'KeyboardDebounce-9.8.7-win-x64.exe'
    $versionedReleaseZip = Join-Path $fixtureReleaseDirectory 'KeyboardDebounce-9.8.7-win-x64.zip'
    $checksumPath = Join-Path $fixtureReleaseDirectory 'KeyboardDebounce-9.8.7-win-x64.sha256'
    $releasePaths = @($versionedReleaseExe, $versionedReleaseZip, $checksumPath)
    if (-not (Test-Path -LiteralPath $versionedReleaseExe -PathType Leaf)) {
        throw "The publish script did not derive the release file name from the project version: $versionedReleaseExe"
    }
    Write-Host 'PASS: publish derives the release file name from KeyboardDebounce.csproj.'

    $publishCall = @(Get-Content -LiteralPath $fakeDotNetLog | Where-Object { $_ -match '^publish ' })
    Assert-Equal 1 $publishCall.Count 'The publish script must invoke dotnet publish exactly once.'
    if ($publishCall[0] -notmatch '(?:^|\s)--self-contained\s+true(?:\s|$)') {
        throw "The publish script must create a self-contained release. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:SelfContained=true(?:\s|$)') {
        throw "The publish script must pin SelfContained property. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:WindowsPackageType=None(?:\s|$)') {
        throw "The publish script must set WindowsPackageType=None. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:WindowsAppSDKSelfContained=true(?:\s|$)') {
        throw "The publish script must set WindowsAppSDKSelfContained=true. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:EnableMsixTooling=true(?:\s|$)') {
        throw "The publish script must set EnableMsixTooling=true. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:PublishSingleFile=true(?:\s|$)') {
        throw "The publish script must create a single-file release. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:IncludeAllContentForSelfExtract=true(?:\s|$)') {
        throw "The publish script must set IncludeAllContentForSelfExtract=true. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:IncludeNativeLibrariesForSelfExtract=true(?:\s|$)') {
        throw "The publish script must set IncludeNativeLibrariesForSelfExtract=true. Actual command: $($publishCall[0])"
    }
    if ($publishCall[0] -notmatch '(?:^|\s)-p:AssemblyName=KeyboardDebounce-9\.8\.7-win-x64(?:\s|$)') {
        throw "The publish script must build the WinUI executable with its final release name so PRI lookup remains valid. Actual command: $($publishCall[0])"
    }
    Write-Host 'PASS: publish requests a self-contained single-file release.'

    $env:FAKE_DOTNET_PUBLISH_SIDECAR = '1'
    $sidecarError = $null
    try {
        & (Join-Path $fixtureScriptDirectory 'publish.ps1')
    }
    catch {
        $sidecarError = $_
    }
    if ($null -eq $sidecarError -or $sidecarError.Exception.Message -notmatch 'sidecar artifacts') {
        throw 'The publish contract must reject sidecar artifacts in publish output.'
    }
    Assert-FileSnapshotsUnchanged -Snapshots $releaseSnapshots -Message 'A sidecar publish output must fail before changing release artifacts.'
    Assert-NoReleaseTransactionFiles -ReleaseDirectory $fixtureReleaseDirectory
    Write-Host 'PASS: publish rejects sidecar artifacts in publish output and keeps release rollback-safe.'
    $env:FAKE_DOTNET_PUBLISH_SIDECAR = ''

    $expandedRelease = Join-Path $fixtureRoot 'expanded-release'
    Expand-Archive -LiteralPath $versionedReleaseZip -DestinationPath $expandedRelease -Force
    $packagedLicense = Join-Path $expandedRelease 'LICENSE'
    if (-not (Test-Path -LiteralPath $packagedLicense -PathType Leaf)) {
        throw 'The release ZIP must include LICENSE.'
    }
    Assert-Equal (Get-Content -Raw -LiteralPath (Join-Path $fixtureRoot 'LICENSE')) (Get-Content -Raw -LiteralPath $packagedLicense) 'The packaged LICENSE must match the repository license.'
    Write-Host 'PASS: release ZIP includes the repository LICENSE.'

    if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw 'The publish script must create one SHA256 checksum file for the release assets.'
    }
    $checksumLines = @(Get-Content -LiteralPath $checksumPath)
    Assert-Equal 2 $checksumLines.Count 'The SHA256 checksum file must cover the EXE and ZIP.'
    foreach ($artifact in @($versionedReleaseExe, $versionedReleaseZip)) {
        $expectedChecksumLine = '{0}  {1}' -f (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash, (Split-Path -Leaf $artifact)
        if ($checksumLines -notcontains $expectedChecksumLine) {
            throw "Missing or incorrect SHA256 entry: $expectedChecksumLine"
        }
    }
    Write-Host 'PASS: SHA256 checksum file covers the EXE and ZIP.'

    $releaseSnapshots = Get-FileSnapshots -Paths $releasePaths
    $invalidPublishedExe = Join-Path $fixtureRoot 'invalid-published.exe'
    'not a Windows executable' | Set-Content -LiteralPath $invalidPublishedExe -Encoding Ascii
    $env:FAKE_PUBLISHED_EXE = $invalidPublishedExe
    $stagingError = $null
    try {
        & (Join-Path $fixtureScriptDirectory 'publish.ps1')
    }
    catch {
        $stagingError = $_
    }
    if ($null -eq $stagingError -or $stagingError.Exception.Message -notmatch 'FileVersion') {
        throw 'An invalid staged executable must fail before the existing release is updated.'
    }
    Assert-FileSnapshotsUnchanged -Snapshots $releaseSnapshots -Message 'A staging failure must leave the existing release intact.'
    Assert-NoReleaseTransactionFiles -ReleaseDirectory $fixtureReleaseDirectory
    $env:FAKE_PUBLISHED_EXE = $fakePublishedExe
    Write-Host 'PASS: staging failure leaves every existing release artifact unchanged.'

    $releaseSnapshots = Get-FileSnapshots -Paths $releasePaths
    $lockedTargetStream = [System.IO.File]::Open(
        $versionedReleaseExe,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $lockedTargetError = $null
        try {
            & (Join-Path $fixtureScriptDirectory 'publish.ps1')
        }
        catch {
            $lockedTargetError = $_
        }
        if ($null -eq $lockedTargetError -or
            $lockedTargetError.Exception.Message -notmatch 'cannot be replaced' -or
            -not $lockedTargetError.Exception.Message.Contains($versionedReleaseExe)) {
            throw "A locked target must fail with the exact target path. Actual error: $($lockedTargetError.Exception.Message)"
        }
        Assert-FileSnapshotsUnchanged -Snapshots $releaseSnapshots -Message 'A locked target must leave the existing release intact.'
    }
    finally {
        $lockedTargetStream.Dispose()
    }
    Assert-NoReleaseTransactionFiles -ReleaseDirectory $fixtureReleaseDirectory
    Write-Host 'PASS: a locked release target fails clearly without terminating its owner or changing the release.'

    . (Join-Path $fixtureScriptDirectory 'publish.ps1') -FunctionsOnly
    $commitStagingDirectory = Join-Path $fixtureRoot 'commit-staging'
    New-Item -ItemType Directory -Path $commitStagingDirectory -Force | Out-Null
    $commitMappings = @()
    foreach ($targetPath in $releasePaths) {
        $stagedPath = Join-Path $commitStagingDirectory (Split-Path -Leaf $targetPath)
        Copy-Item -LiteralPath $targetPath -Destination $stagedPath
        $commitMappings += [pscustomobject]@{ StagedPath = $stagedPath; TargetPath = $targetPath }
    }
    Assert-ReleaseTargetsReplaceable -Mappings $commitMappings -ReleaseDirectory $fixtureReleaseDirectory
    $releaseSnapshots = Get-FileSnapshots -Paths $releasePaths
    $commitLock = [System.IO.File]::Open(
        $versionedReleaseZip,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $commitError = $null
        try {
            Commit-ReleaseArtifacts -Mappings $commitMappings
        }
        catch {
            $commitError = $_
        }
        if ($null -eq $commitError -or
            $commitError.Exception.Message -notmatch 'restored' -or
            -not $commitError.Exception.Message.Contains($versionedReleaseZip)) {
            throw "A commit-stage failure must identify its target and confirm rollback. Actual error: $($commitError.Exception.Message)"
        }
        Assert-FileSnapshotsUnchanged -Snapshots $releaseSnapshots -Message 'A commit-stage failure must restore the complete old release.'
    }
    finally {
        $commitLock.Dispose()
    }
    Assert-NoReleaseTransactionFiles -ReleaseDirectory $fixtureReleaseDirectory
    Write-Host 'PASS: a commit-stage failure rolls back every release artifact.'

    $fixtureProjectXml = [xml](Get-Content -Raw -LiteralPath $fixtureProjectPath)
    $fixtureProjectXml.Project.PropertyGroup.Version = '0.1.0'
    $fixtureProjectXml.Save($fixtureProjectPath)
    $versionMismatchError = $null
    try {
        & (Join-Path $fixtureScriptDirectory 'publish.ps1')
    }
    catch {
        $versionMismatchError = $_
    }
    if ($null -eq $versionMismatchError) {
        throw 'The publish script must reject an executable whose FileVersion does not match the project version.'
    }
    if ($versionMismatchError.Exception.Message -notmatch 'FileVersion') {
        throw "The FileVersion validation error must identify the mismatch. Actual error: $($versionMismatchError.Exception.Message)"
    }
    $mismatchedReleaseExe = Join-Path $fixtureReleaseDirectory 'KeyboardDebounce-0.1.0-win-x64.exe'
    if (Test-Path -LiteralPath $mismatchedReleaseExe) {
        throw 'A FileVersion mismatch must fail before copying an EXE into releases.'
    }
    Write-Host 'PASS: publish rejects an EXE whose FileVersion differs from the project version.'

    $verifyFixtureRoot = Join-Path $fixtureRoot 'verify-fixture'
    $verifyFixtureScripts = Join-Path $verifyFixtureRoot 'scripts'
    $verifyFixtureTests = Join-Path $verifyFixtureRoot 'tests\KeyboardDebounce.Tests'
    New-Item -ItemType Directory -Path $verifyFixtureScripts, $verifyFixtureTests -Force | Out-Null
    Copy-Item -LiteralPath $verifyScriptPath -Destination (Join-Path $verifyFixtureScripts 'verify.ps1')
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'KeyboardDebounce.csproj') -Destination (Join-Path $verifyFixtureRoot 'KeyboardDebounce.csproj')
    Copy-Item -LiteralPath (Join-Path $fixtureTestProjectDirectory 'KeyboardDebounce.Tests.csproj') -Destination (Join-Path $verifyFixtureTests 'KeyboardDebounce.Tests.csproj')
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'NuGet.Config') -Destination (Join-Path $verifyFixtureRoot 'NuGet.Config')
    @'
[System.IO.File]::AppendAllText($env:VERIFY_CONTRACT_LOG, "contract-tests-called" + [Environment]::NewLine)
'@ | Set-Content -LiteralPath (Join-Path $verifyFixtureRoot 'tests\PublishScript.Tests.ps1') -Encoding UTF8
    $verifyContractLog = Join-Path $verifyFixtureRoot 'contract-tests.log'
    $verifyDotNetLog = Join-Path $verifyFixtureRoot 'dotnet.log'
    $env:VERIFY_CONTRACT_LOG = $verifyContractLog
    $env:FAKE_DOTNET_LOG = $verifyDotNetLog
    $env:FAKE_DOTNET_EXIT_CODE = '0'
    $env:FAKE_DOTNET_FAIL_COMMAND = ''

    & (Join-Path $verifyFixtureScripts 'verify.ps1')

    $verifyDotNetCalls = @(Get-Content -LiteralPath $verifyDotNetLog)
    Assert-Equal 1 @($verifyDotNetCalls | Where-Object { $_ -match '^build ' }).Count 'verify.ps1 must run dotnet build once.'
    Assert-Equal 1 @($verifyDotNetCalls | Where-Object { $_ -match '^test ' }).Count 'verify.ps1 must run dotnet test once.'
    Assert-Equal 0 @($verifyDotNetCalls | Where-Object { $_ -match '^publish ' }).Count 'verify.ps1 must not run publish.ps1.'
    Assert-Equal 1 @(Get-Content -LiteralPath $verifyContractLog).Count 'verify.ps1 must directly run the publish contract tests once.'
    Write-Host 'PASS: verify.ps1 executes both test suites exactly once in an isolated fixture.'

    $fixtureProjectXml = [xml](Get-Content -Raw -LiteralPath $fixtureProjectPath)
    $fixtureProjectXml.Project.PropertyGroup.Version = '9.8'
    $fixtureProjectXml.Save($fixtureProjectPath)
    Clear-Content -LiteralPath $fakeDotNetLog
    $env:FAKE_DOTNET_LOG = $fakeDotNetLog
    $invalidVersionError = $null
    try {
        & (Join-Path $fixtureScriptDirectory 'publish.ps1')
    }
    catch {
        $invalidVersionError = $_
    }
    if ($null -eq $invalidVersionError -or $invalidVersionError.Exception.Message -notmatch 'Major.Minor.Patch') {
        throw 'The publish script must reject a project Version that is not numeric Major.Minor.Patch.'
    }
    Assert-Equal 0 @(Get-Content -LiteralPath $fakeDotNetLog).Count 'Invalid project Version must fail before invoking dotnet.'
    Write-Host 'PASS: publish validates the project Version before invoking dotnet.'
}
finally {
    $env:PATH = $originalPath
    foreach ($name in $publishEnvironmentVariableNames) {
        [Environment]::SetEnvironmentVariable($name, $originalPublishEnvironment[$name], 'Process')
    }
    foreach ($name in $testEnvironmentVariableNames) {
        [Environment]::SetEnvironmentVariable($name, $originalTestEnvironment[$name], 'Process')
    }
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
