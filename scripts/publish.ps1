$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$version = '0.1.0'
$runtime = 'win-x64'
$project = Join-Path $root 'KeyboardDebounce.csproj'
$testProject = Join-Path $root 'tests\KeyboardDebounce.Tests\KeyboardDebounce.Tests.csproj'
$nugetConfig = Join-Path $root 'NuGet.Config'
$dist = Join-Path $root 'dist'
$publishDir = Join-Path $root 'dist\publish'
$releaseDir = Join-Path $root 'releases'
$releaseBase = "KeyboardDebounce-$version-$runtime"
$releaseExe = Join-Path $releaseDir "$releaseBase.exe"
$releaseZip = Join-Path $releaseDir "$releaseBase.zip"

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $dist 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $dist 'nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $dist 'nuget-http-cache'
$env:APPDATA = Join-Path $dist 'appdata'
$env:LOCALAPPDATA = Join-Path $dist 'localappdata'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

dotnet restore $project --configfile $nugetConfig
dotnet restore $testProject --configfile $nugetConfig
dotnet build $project -c Release --no-restore
dotnet test $testProject -c Release --no-restore
dotnet publish $project -c Release -r $runtime --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

$publishedExe = Join-Path $publishDir 'KeyboardDebounce.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Published exe not found: $publishedExe"
}

Copy-Item -LiteralPath $publishedExe -Destination $releaseExe -Force
if (Test-Path $releaseZip) {
    Remove-Item -LiteralPath $releaseZip -Force
}

$zipRoot = Join-Path $root 'dist\release-zip'
if (Test-Path $zipRoot) {
    Remove-Item -LiteralPath $zipRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $zipRoot -Force | Out-Null
Copy-Item -LiteralPath $releaseExe -Destination (Join-Path $zipRoot "$releaseBase.exe")
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $zipRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $root 'assets\keyboard-debounce.ico') -Destination (Join-Path $zipRoot 'keyboard-debounce.ico')
Compress-Archive -Path (Join-Path $zipRoot '*') -DestinationPath $releaseZip -Force

Write-Host "Release exe: $releaseExe"
Write-Host "Release zip: $releaseZip"
