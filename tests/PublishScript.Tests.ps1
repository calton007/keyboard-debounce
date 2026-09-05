$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repositoryRoot 'scripts\publish.ps1') -FunctionsOnly
function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
$version = Get-ProjectVersion (Join-Path $repositoryRoot 'src-tauri\Cargo.toml')
Assert-True ($version -eq '0.3.0') 'Cargo package version must be 0.3.0'
$config = Get-Content -Raw (Join-Path $repositoryRoot 'src-tauri\tauri.conf.json') | ConvertFrom-Json
Assert-True (-not $config.PSObject.Properties['version']) 'Tauri must use Cargo as the version source'
Assert-True ($config.app.windows.Count -eq 0) 'Silent start must not eagerly create a WebView'
Assert-True ($config.bundle.windows.nsis.installMode -eq 'currentUser') 'Installer must use current user scope'
Assert-True ($config.bundle.windows.webviewInstallMode.type -eq 'downloadBootstrapper') 'Installer must provision WebView2'
$publishSource = Get-Content -Raw (Join-Path $repositoryRoot 'scripts\publish.ps1')
Assert-True ($publishSource -notmatch '(?i)\bStop-Process\b|\btaskkill\b') 'Publisher must not terminate running apps'
$fixtureRoot = Join-Path $repositoryRoot ('artifacts\publish-tests-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $fixtureRoot 'staged'
$release = Join-Path $fixtureRoot 'releases'
New-Item -ItemType Directory -Force -Path $stage, $release | Out-Null
$mappings = foreach ($name in @('app.exe','app.zip','app-setup.exe','app.sha256')) {
    $source = Join-Path $stage $name
    $destination = Join-Path $release $name
    [IO.File]::WriteAllText($source, "new-$name")
    [IO.File]::WriteAllText($destination, "old-$name")
    [pscustomobject]@{ StagedPath=$source; TargetPath=$destination }
}
Assert-ReleaseTargetsReplaceable -Mappings $mappings -ReleaseDirectory $release
Commit-ReleaseArtifacts -Mappings $mappings
foreach ($mapping in $mappings) { Assert-True ((Get-FileHash $mapping.TargetPath).Hash -eq (Get-FileHash $mapping.StagedPath).Hash) 'Committed artifact hash mismatch' }
Write-Host 'PASS: four-artifact transactional publication'
$before = @($mappings | ForEach-Object { (Get-FileHash $_.TargetPath).Hash })
$lockedPath = $mappings[0].TargetPath
$locked = [IO.File]::Open($lockedPath, 'Open', 'ReadWrite', 'None')
$blocked = $false
try { Assert-ReleaseTargetsReplaceable -Mappings $mappings -ReleaseDirectory $release }
catch { $blocked = $_.Exception.Message.Contains($lockedPath) }
finally { $locked.Dispose() }
Assert-True $blocked 'Locked artifact must report its exact path'
for ($i=0; $i -lt $mappings.Count; $i++) { Assert-True ((Get-FileHash $mappings[$i].TargetPath).Hash -eq $before[$i]) 'Locked publication changed an existing artifact' }
Write-Host 'PASS: locked artifacts preserve previous release'
$brokenMappings = @($mappings) + @([pscustomobject]@{StagedPath=(Join-Path $stage 'missing');TargetPath=(Join-Path $release 'missing')})
$failed = $false
try { Commit-ReleaseArtifacts -Mappings $brokenMappings } catch { $failed = $true }
Assert-True $failed 'Missing staged artifact must fail'
for ($i=0; $i -lt $mappings.Count; $i++) { Assert-True ((Get-FileHash $mappings[$i].TargetPath).Hash -eq $before[$i]) 'Prepare failure changed an existing artifact' }
# Force failure after the first backup has moved: a later target becomes locked.
$lateLock = [IO.File]::Open($mappings[1].TargetPath, 'Open', 'ReadWrite', 'None')
$failed = $false
try { Commit-ReleaseArtifacts -Mappings $mappings } catch { $failed = $true }
finally { $lateLock.Dispose() }
Assert-True $failed 'Mid-commit lock must fail'
for ($i=0; $i -lt $mappings.Count; $i++) { Assert-True ((Get-FileHash $mappings[$i].TargetPath).Hash -eq $before[$i]) 'Rollback did not restore previous artifacts' }
Write-Host 'PASS: prepare/commit failure recovery'
$checksum = Join-Path $stage 'hashes.sha256'
Get-ChecksumLines @($mappings[0].StagedPath, $mappings[1].StagedPath, $mappings[2].StagedPath) | Set-Content -LiteralPath $checksum
Assert-ChecksumFile -ChecksumPath $checksum -ArtifactPaths @($mappings[0].StagedPath, $mappings[1].StagedPath, $mappings[2].StagedPath)
$failed = $false
[IO.File]::AppendAllText($mappings[0].StagedPath, 'tampered')
try { Assert-ChecksumFile -ChecksumPath $checksum -ArtifactPaths @($mappings[0].StagedPath, $mappings[1].StagedPath, $mappings[2].StagedPath) }
catch { $failed = $_.Exception.Message.Contains('mismatch') }
Assert-True $failed 'Modified staged executable must fail checksum verification'
$failed = $false
try { Assert-NativeX64Executable $mappings[0].StagedPath }
catch { $failed = $_.Exception.Message.Contains($mappings[0].StagedPath) }
Assert-True $failed 'Non-PE portable artifact must be rejected with its path'
Write-Host "PASS: version, lifecycle, installer, checksums. Fixtures retained: $fixtureRoot"
