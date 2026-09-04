[CmdletBinding()]
param(
    [switch] $FunctionsOnly
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$runtime = 'win-x64'
$project = Join-Path $root 'KeyboardDebounce.csproj'
$testProject = Join-Path $root 'tests\KeyboardDebounce.Tests\KeyboardDebounce.Tests.csproj'
$nugetConfig = Join-Path $root 'NuGet.Config'
$dist = Join-Path $root 'dist'
$stagingParent = Join-Path $dist 'release-staging'
$releaseDir = Join-Path $root 'releases'

function Get-ProjectVersion {
    param([string] $ProjectPath)

    $projectXml = [xml](Get-Content -Raw -LiteralPath $ProjectPath)
    $versionNodes = @($projectXml.SelectNodes('/Project/PropertyGroup/Version'))
    if ($versionNodes.Count -ne 1) {
        throw "Expected exactly one Version element in $ProjectPath; found $($versionNodes.Count)."
    }

    $projectVersion = ([string] $versionNodes[0].InnerText).Trim()
    if ($projectVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "Project Version must use Major.Minor.Patch numeric format; found '$projectVersion' in $ProjectPath."
    }

    return $projectVersion
}

function Invoke-DotNet {
    & dotnet @args
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "dotnet $($args -join ' ') failed with exit code $exitCode."
    }
}

function Get-NonEmptyFile {
    param(
        [string] $Path,
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description not found: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 0) {
        throw "$Description is empty: $Path"
    }

    return $item
}

function Get-ChecksumLines {
    param([string[]] $ArtifactPaths)

    foreach ($artifactPath in $ArtifactPaths) {
        $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash
        '{0}  {1}' -f $hash, (Split-Path -Leaf $artifactPath)
    }
}

function Assert-SingleFilePublishedArtifacts {
    param(
        [string] $PublishDirectory,
        [string] $ExpectedExeName
    )
    $publishedFiles = Get-ChildItem -LiteralPath $PublishDirectory -File

    if ([string]::IsNullOrWhiteSpace($ExpectedExeName)) {
        throw 'Expected single-file executable name cannot be empty.'
    }

    if (-not ($publishedFiles | Where-Object { $_.Name -ieq $ExpectedExeName })) {
        throw "Single-file publish output must include $ExpectedExeName in $PublishDirectory."
    }

    $unexpectedArtifacts = @(
        foreach ($file in $publishedFiles) {
            if ($file.Name -ieq $ExpectedExeName) {
                continue
            }
            if ($file.Extension -ieq '.pdb') {
                continue
            }
            $file
        }
    )

    if ($unexpectedArtifacts.Count -ne 0) {
        $names = @($unexpectedArtifacts | ForEach-Object { $_.Name }) -join ', '
        throw "Publish produced sidecar artifacts for a single-file release: $names"
    }
}

function Assert-ChecksumFile {
    param(
        [string] $ChecksumPath,
        [string[]] $ArtifactPaths
    )

    [void](Get-NonEmptyFile $ChecksumPath 'SHA256 checksum file')
    $expectedLines = @(Get-ChecksumLines -ArtifactPaths $ArtifactPaths)
    $actualLines = @(Get-Content -LiteralPath $ChecksumPath)

    if ($actualLines.Count -ne $expectedLines.Count) {
        throw "SHA256 checksum file must contain $($expectedLines.Count) entries; found $($actualLines.Count): $ChecksumPath"
    }

    for ($index = 0; $index -lt $expectedLines.Count; $index++) {
        if ($actualLines[$index] -cne $expectedLines[$index]) {
            throw "SHA256 checksum mismatch in ${ChecksumPath}: expected '$($expectedLines[$index])', found '$($actualLines[$index])'."
        }
    }
}

function Assert-StagedRelease {
    param(
        [string] $StagedExe,
        [string] $StagedZip,
        [string] $StagedChecksum,
        [string] $ValidationDirectory
    )

    [void](Get-NonEmptyFile $StagedExe 'Staged release exe')
    [void](Get-NonEmptyFile $StagedZip 'Staged release zip')
    Assert-ChecksumFile -ChecksumPath $StagedChecksum -ArtifactPaths @($StagedExe, $StagedZip)

    New-Item -ItemType Directory -Path $ValidationDirectory -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $StagedZip -DestinationPath $ValidationDirectory -Force
    }
    catch {
        throw "Staged release zip could not be expanded: $StagedZip. $($_.Exception.Message)"
    }

    $expandedExe = Join-Path $ValidationDirectory (Split-Path -Leaf $StagedExe)
    foreach ($requiredPath in @(
        $expandedExe,
        (Join-Path $ValidationDirectory 'README.md'),
        (Join-Path $ValidationDirectory 'LICENSE'),
        (Join-Path $ValidationDirectory 'keyboard-debounce.ico')
    )) {
        [void](Get-NonEmptyFile $requiredPath 'Required staged ZIP entry')
    }

    $stagedExeHash = (Get-FileHash -LiteralPath $StagedExe -Algorithm SHA256).Hash
    $expandedExeHash = (Get-FileHash -LiteralPath $expandedExe -Algorithm SHA256).Hash
    if ($expandedExeHash -cne $stagedExeHash) {
        throw "The executable inside the staged ZIP does not match the staged release executable: $StagedZip"
    }
}

function Assert-ReleaseTargetsReplaceable {
    param(
        [object[]] $Mappings,
        [string] $ReleaseDirectory
    )

    $probePath = Join-Path $ReleaseDirectory ('.publish-write-probe-{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
    $probeStream = $null
    try {
        $probeStream = [System.IO.File]::Open(
            $probePath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch {
        throw "Release directory is not writable: $ReleaseDirectory. $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $probeStream) {
            $probeStream.Dispose()
        }
        if (Test-Path -LiteralPath $probePath) {
            Microsoft.PowerShell.Management\Remove-Item -LiteralPath $probePath -Force
        }
    }

    foreach ($mapping in $Mappings) {
        $targetPath = [string] $mapping.TargetPath
        if (-not (Test-Path -LiteralPath $targetPath)) {
            continue
        }
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            throw "Release target cannot be replaced because it is not a file: $targetPath"
        }

        $targetStream = $null
        try {
            $targetStream = [System.IO.File]::Open(
                $targetPath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
        }
        catch {
            throw "Release target cannot be replaced (it may be locked/in use, read-only, or access is denied): $targetPath. $($_.Exception.Message)"
        }
        finally {
            if ($null -ne $targetStream) {
                $targetStream.Dispose()
            }
        }
    }
}

function Commit-ReleaseArtifacts {
    param([object[]] $Mappings)

    $transactionId = [Guid]::NewGuid().ToString('N')
    $entries = @(
        foreach ($mapping in $Mappings) {
            $targetPath = [string] $mapping.TargetPath
            $targetDirectory = Split-Path -Parent $targetPath
            $targetName = Split-Path -Leaf $targetPath
            [pscustomobject]@{
                StagedPath = [string] $mapping.StagedPath
                TargetPath = $targetPath
                PendingPath = Join-Path $targetDirectory ('.{0}.{1}.pending' -f $targetName, $transactionId)
                BackupPath = Join-Path $targetDirectory ('.{0}.{1}.backup' -f $targetName, $transactionId)
                TargetExisted = Test-Path -LiteralPath $targetPath -PathType Leaf
                BackupCreated = $false
                Installed = $false
            }
        }
    )

    $currentTarget = '<unknown>'
    $officialUpdateStarted = $false
    $commitSucceeded = $false

    try {
        foreach ($entry in $entries) {
            $currentTarget = $entry.TargetPath
            try {
                Copy-Item -LiteralPath $entry.StagedPath -Destination $entry.PendingPath
                [void](Get-NonEmptyFile $entry.PendingPath 'Pending release artifact')
                $stagedHash = (Get-FileHash -LiteralPath $entry.StagedPath -Algorithm SHA256).Hash
                $pendingHash = (Get-FileHash -LiteralPath $entry.PendingPath -Algorithm SHA256).Hash
                if ($pendingHash -cne $stagedHash) {
                    throw "Pending artifact hash does not match its staged source."
                }
            }
            catch {
                throw "Failed to prepare release target '$currentTarget' from staged artifact '$($entry.StagedPath)': $($_.Exception.Message)"
            }
        }

        $officialUpdateStarted = $true
        foreach ($entry in $entries) {
            $currentTarget = $entry.TargetPath
            if ($entry.TargetExisted) {
                try {
                    Microsoft.PowerShell.Management\Move-Item -LiteralPath $entry.TargetPath -Destination $entry.BackupPath
                    $entry.BackupCreated = $true
                }
                catch {
                    throw "Failed to back up release target '$currentTarget' before replacement: $($_.Exception.Message)"
                }
            }
        }

        # Install the executable last so it cannot be launched while its ZIP and checksum are incomplete.
        $installationEntries = @(
            $entries | Where-Object { [System.IO.Path]::GetExtension($_.TargetPath) -ine '.exe' }
            $entries | Where-Object { [System.IO.Path]::GetExtension($_.TargetPath) -ieq '.exe' }
        )
        foreach ($entry in $installationEntries) {
            $currentTarget = $entry.TargetPath
            try {
                Microsoft.PowerShell.Management\Move-Item -LiteralPath $entry.PendingPath -Destination $entry.TargetPath
                $entry.Installed = $true
            }
            catch {
                throw "Failed to install staged artifact as release target '$currentTarget': $($_.Exception.Message)"
            }
        }

        foreach ($entry in $entries) {
            $currentTarget = $entry.TargetPath
            [void](Get-NonEmptyFile $entry.TargetPath 'Committed release artifact')
            $stagedHash = (Get-FileHash -LiteralPath $entry.StagedPath -Algorithm SHA256).Hash
            $committedHash = (Get-FileHash -LiteralPath $entry.TargetPath -Algorithm SHA256).Hash
            if ($committedHash -cne $stagedHash) {
                throw "Committed release target does not match its staged source: $currentTarget"
            }
        }

        $commitSucceeded = $true
    }
    catch {
        $commitFailure = $_
        $rollbackFailures = @()

        for ($index = $entries.Count - 1; $index -ge 0; $index--) {
            $entry = $entries[$index]
            if (-not $entry.Installed) {
                continue
            }
            try {
                if (Test-Path -LiteralPath $entry.TargetPath) {
                    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $entry.TargetPath -Force
                }
                $entry.Installed = $false
            }
            catch {
                $rollbackFailures += "Could not remove partially committed target '$($entry.TargetPath)': $($_.Exception.Message)"
            }
        }

        for ($index = $entries.Count - 1; $index -ge 0; $index--) {
            $entry = $entries[$index]
            if (-not $entry.BackupCreated) {
                continue
            }
            try {
                if (Test-Path -LiteralPath $entry.TargetPath) {
                    throw "The target path is still occupied."
                }
                Microsoft.PowerShell.Management\Move-Item -LiteralPath $entry.BackupPath -Destination $entry.TargetPath
                $entry.BackupCreated = $false
            }
            catch {
                $rollbackFailures += "Could not restore backup '$($entry.BackupPath)' to '$($entry.TargetPath)': $($_.Exception.Message)"
            }
        }

        if ($rollbackFailures.Count -gt 0) {
            $rollbackDetails = $rollbackFailures -join [Environment]::NewLine
            throw "Failed to commit release target '$currentTarget'. Rollback was incomplete; backup files were kept for manual recovery. Original error: $($commitFailure.Exception.Message)$([Environment]::NewLine)$rollbackDetails"
        }

        if ($officialUpdateStarted) {
            throw "Failed to commit release target '$currentTarget'. Existing release artifacts were restored. Original error: $($commitFailure.Exception.Message)"
        }

        throw "Failed to prepare release target '$currentTarget'. Existing release artifacts were not changed. Original error: $($commitFailure.Exception.Message)"
    }
    finally {
        foreach ($entry in $entries) {
            if (Test-Path -LiteralPath $entry.PendingPath) {
                try {
                    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $entry.PendingPath -Force
                }
                catch {
                    Write-Warning "Could not remove pending release artifact: $($entry.PendingPath). $($_.Exception.Message)"
                }
            }
        }

        if ($commitSucceeded) {
            foreach ($entry in $entries) {
                if (Test-Path -LiteralPath $entry.BackupPath) {
                    try {
                        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $entry.BackupPath -Force
                    }
                    catch {
                        Write-Warning "Release succeeded, but the backup could not be removed: $($entry.BackupPath). $($_.Exception.Message)"
                    }
                }
            }
        }
    }
}

function Invoke-Publish {
    $version = Get-ProjectVersion $project
    $releaseBase = "KeyboardDebounce-$version-$runtime"
    $releaseExe = Join-Path $releaseDir "$releaseBase.exe"
    $releaseZip = Join-Path $releaseDir "$releaseBase.zip"
    $releaseChecksum = Join-Path $releaseDir "$releaseBase.sha256"
    $stageRoot = Join-Path $stagingParent ('{0}-{1}-{2}' -f $releaseBase, $PID, [Guid]::NewGuid().ToString('N'))
    $publishDir = Join-Path $stageRoot 'publish'
    $packageRoot = Join-Path $stageRoot 'package'
    $artifactDirectory = Join-Path $stageRoot 'artifacts'
    $validationDirectory = Join-Path $stageRoot 'validation'
    $stagedExe = Join-Path $artifactDirectory "$releaseBase.exe"
    $stagedZip = Join-Path $artifactDirectory "$releaseBase.zip"
    $stagedChecksum = Join-Path $artifactDirectory "$releaseBase.sha256"

    $environmentVariableNames = @(
        'DOTNET_CLI_HOME',
        'NUGET_PACKAGES',
        'NUGET_HTTP_CACHE_PATH',
        'APPDATA',
        'LOCALAPPDATA',
        'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
        'DOTNET_CLI_TELEMETRY_OPTOUT'
    )
    $originalEnvironment = @{}
    foreach ($name in $environmentVariableNames) {
        $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    try {
        New-Item -ItemType Directory -Path $publishDir, $packageRoot, $artifactDirectory -Force | Out-Null

        $env:DOTNET_CLI_HOME = Join-Path $dist 'dotnet-home'
        $env:NUGET_PACKAGES = Join-Path $dist 'nuget-packages'
        $env:NUGET_HTTP_CACHE_PATH = Join-Path $dist 'nuget-http-cache'
        $env:APPDATA = Join-Path $dist 'appdata'
        $env:LOCALAPPDATA = Join-Path $dist 'localappdata'
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

        Invoke-DotNet restore $project --configfile $nugetConfig
        Invoke-DotNet restore $testProject --configfile $nugetConfig
        Invoke-DotNet build $project -c Release --no-restore
        Invoke-DotNet test $testProject -c Release --no-restore
        Invoke-DotNet publish $project -c Release -r $runtime --self-contained true `
            '-p:SelfContained=true' `
            '-p:WindowsPackageType=None' `
            '-p:WindowsAppSDKSelfContained=true' `
            '-p:EnableMsixTooling=true' `
            '-p:PublishSingleFile=true' `
            '-p:IncludeAllContentForSelfExtract=true' `
            '-p:IncludeNativeLibrariesForSelfExtract=true' `
            "-p:AssemblyName=$releaseBase" `
            -o $publishDir
        $publishedExeName = "$releaseBase.exe"
        Assert-SingleFilePublishedArtifacts `
            -PublishDirectory $publishDir `
            -ExpectedExeName $publishedExeName

        $publishedExe = Join-Path $publishDir $publishedExeName
        $publishedExeItem = Get-NonEmptyFile $publishedExe 'Published exe'
        $publishedVersionInfo = $publishedExeItem.VersionInfo
        if ([string]::IsNullOrWhiteSpace($publishedVersionInfo.FileVersion)) {
            throw "Published exe has no FileVersion: $publishedExe"
        }

        $actualFileVersion = '{0}.{1}.{2}.{3}' -f `
            $publishedVersionInfo.FileMajorPart, `
            $publishedVersionInfo.FileMinorPart, `
            $publishedVersionInfo.FileBuildPart, `
            $publishedVersionInfo.FilePrivatePart
        $expectedFileVersion = ([Version] "$version.0").ToString(4)
        if ($actualFileVersion -ne $expectedFileVersion) {
            throw "Published exe FileVersion '$actualFileVersion' does not match project Version '$version' (expected '$expectedFileVersion'): $publishedExe"
        }

        Copy-Item -LiteralPath $publishedExe -Destination $stagedExe
        Copy-Item -LiteralPath $stagedExe -Destination (Join-Path $packageRoot "$releaseBase.exe")
        Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $packageRoot 'README.md')
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE')
        Copy-Item -LiteralPath (Join-Path $root 'assets\keyboard-debounce.ico') -Destination (Join-Path $packageRoot 'keyboard-debounce.ico')
        Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $stagedZip

        $checksumLines = @(Get-ChecksumLines -ArtifactPaths @($stagedExe, $stagedZip))
        $checksumLines | Set-Content -LiteralPath $stagedChecksum -Encoding Ascii
        Assert-StagedRelease `
            -StagedExe $stagedExe `
            -StagedZip $stagedZip `
            -StagedChecksum $stagedChecksum `
            -ValidationDirectory $validationDirectory

        New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
        $releaseMappings = @(
            [pscustomobject]@{ StagedPath = $stagedExe; TargetPath = $releaseExe },
            [pscustomobject]@{ StagedPath = $stagedZip; TargetPath = $releaseZip },
            [pscustomobject]@{ StagedPath = $stagedChecksum; TargetPath = $releaseChecksum }
        )
        Assert-ReleaseTargetsReplaceable -Mappings $releaseMappings -ReleaseDirectory $releaseDir
        Commit-ReleaseArtifacts -Mappings $releaseMappings

        Write-Host "Verified FileVersion: $actualFileVersion"
        Write-Host "Release exe: $releaseExe"
        Write-Host "Release zip: $releaseZip"
        Write-Host "SHA256 checksums: $releaseChecksum"
    }
    finally {
        foreach ($name in $environmentVariableNames) {
            [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process')
        }

        if (Test-Path -LiteralPath $stageRoot) {
            try {
                Microsoft.PowerShell.Management\Remove-Item -LiteralPath $stageRoot -Recurse -Force
            }
            catch {
                Write-Warning "Could not remove release staging directory: $stageRoot. $($_.Exception.Message)"
            }
        }
    }
}

if (-not $FunctionsOnly) {
    Invoke-Publish
}
