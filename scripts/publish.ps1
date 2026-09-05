[CmdletBinding()]
param(
    [switch] $FunctionsOnly
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$runtime = 'win-x64'
$project = Join-Path $root 'src-tauri\Cargo.toml'
$dist = Join-Path $root 'dist'
$stagingParent = Join-Path $dist 'release-staging'
$releaseDir = Join-Path $root 'releases'

function Get-ProjectVersion {
    param([string] $ProjectPath)

    $source = Get-Content -Raw -LiteralPath $ProjectPath
    $packageSection = [regex]::Match($source, '(?ms)^\[package\]\s*(.*?)(?=^\[|\z)').Groups[1].Value
    $versionNodes = [regex]::Matches($packageSection, '(?m)^version\s*=\s*"([^"]+)"\s*$')
    if ($versionNodes.Count -ne 1) {
        throw "Expected exactly one Cargo package version in $ProjectPath; found $($versionNodes.Count)."
    }

    $projectVersion = $versionNodes[0].Groups[1].Value
    if ($projectVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "Project Version must use Major.Minor.Patch numeric format; found '$projectVersion' in $ProjectPath."
    }

    return $projectVersion
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

function Assert-NativeX64Executable {
    param([string] $Path)
    [void](Get-NonEmptyFile $Path 'Native executable')
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5A4D) {
        throw "Invalid executable DOS header: $Path"
    }
    $peOffset = [long][BitConverter]::ToUInt32($bytes, 60)
    if ($peOffset + 264 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550) {
        throw "Invalid executable PE header: $Path"
    }
    if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664 -or
        [BitConverter]::ToUInt16($bytes, $peOffset + 24) -ne 0x20B) {
        throw "Portable executable must be Windows x64 PE32+: $Path"
    }
    # PE32+ data directory 14 is the CLR runtime header. Native Rust must leave it empty.
    if ([BitConverter]::ToUInt64($bytes, $peOffset + 24 + 112 + 14 * 8) -ne 0) {
        throw "Portable executable unexpectedly contains a CLR runtime header: $Path"
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
        [string] $ValidationDirectory,
        [string] $StagedInstaller
    )

    [void](Get-NonEmptyFile $StagedExe 'Staged release exe')
    [void](Get-NonEmptyFile $StagedZip 'Staged release zip')
    [void](Get-NonEmptyFile $StagedInstaller 'Staged NSIS installer')
    Assert-NativeX64Executable $StagedExe
    Assert-ChecksumFile -ChecksumPath $StagedChecksum -ArtifactPaths @($StagedExe, $StagedZip, $StagedInstaller)

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
        (Join-Path $ValidationDirectory 'keyboard-debounce.ico'),
        (Join-Path $ValidationDirectory 'docs\DESIGN.md'),
        (Join-Path $ValidationDirectory 'docs\TAURI-VALIDATION.md')
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
    . (Join-Path $PSScriptRoot 'toolchain.ps1')
    Initialize-Toolchain
    $version = Get-ProjectVersion $project
    $releaseBase = "KeyboardDebounce-$version-$runtime"
    $stageRoot = Join-Path $stagingParent ([Guid]::NewGuid().ToString('N'))
    $packageRoot = Join-Path $stageRoot 'package'
    $artifactDirectory = Join-Path $stageRoot 'artifacts'
    $validationDirectory = Join-Path $stageRoot 'validation'
    New-Item -ItemType Directory -Force -Path $packageRoot, $artifactDirectory, $releaseDir | Out-Null
    $stagedExe = Join-Path $artifactDirectory "$releaseBase.exe"
    $stagedZip = Join-Path $artifactDirectory "$releaseBase.zip"
    $stagedInstaller = Join-Path $artifactDirectory "$releaseBase-setup.exe"
    $stagedChecksum = Join-Path $artifactDirectory "$releaseBase.sha256"
    $lockPath = Join-Path $releaseDir '.publish.lock'
    $publishLock = $null
    Push-Location $root
    try {
        $publishLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        & (Join-Path $PSScriptRoot 'verify.ps1')
        if (-not $?) { throw 'Verification failed' }
        Invoke-Checked 'npm.cmd' @('run', 'tauri', '--', 'build', '--target', 'x86_64-pc-windows-msvc')
        $targetRoot = Join-Path $root 'src-tauri\target\x86_64-pc-windows-msvc\release'
        $publishedExe = Join-Path $targetRoot 'keyboard-debounce.exe'
        $exe = Get-NonEmptyFile $publishedExe 'Portable executable'
        $info = $exe.VersionInfo
        $actual = '{0}.{1}.{2}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart
        if ($actual -ne $version) { throw "Executable version $actual does not match Cargo version $version" }
        $installerDirectory = Join-Path $targetRoot 'bundle\nsis'
        $installers = @(Get-ChildItem -LiteralPath $installerDirectory -Filter "*${version}*x64*setup.exe")
        if ($installers.Count -ne 1) { throw "Expected one NSIS installer in $installerDirectory; found $($installers.Count)" }
        Copy-Item -LiteralPath $publishedExe -Destination $stagedExe
        Copy-Item -LiteralPath $installers[0].FullName -Destination $stagedInstaller
        Copy-Item -LiteralPath $stagedExe -Destination (Join-Path $packageRoot "$releaseBase.exe")
        foreach ($name in @('README.md', 'LICENSE')) {
            Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $packageRoot $name)
        }
        $packageDocs = Join-Path $packageRoot 'docs'
        New-Item -ItemType Directory -Path $packageDocs | Out-Null
        foreach ($name in @('DESIGN.md', 'TAURI-VALIDATION.md')) {
            Copy-Item -LiteralPath (Join-Path (Join-Path $root 'docs') $name) -Destination (Join-Path $packageDocs $name)
        }
        Copy-Item -LiteralPath (Join-Path $root 'assets\keyboard-debounce.ico') -Destination (Join-Path $packageRoot 'keyboard-debounce.ico')
        Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $stagedZip
        Get-ChecksumLines @($stagedExe, $stagedZip, $stagedInstaller) | Set-Content -LiteralPath $stagedChecksum -Encoding ascii
        Assert-StagedRelease -StagedExe $stagedExe -StagedZip $stagedZip -StagedChecksum $stagedChecksum -StagedInstaller $stagedInstaller -ValidationDirectory $validationDirectory
        $mappings = @($stagedExe, $stagedZip, $stagedInstaller, $stagedChecksum) | ForEach-Object {
            [pscustomobject]@{ StagedPath = $_; TargetPath = Join-Path $releaseDir (Split-Path -Leaf $_) }
        }
        Assert-ReleaseTargetsReplaceable -Mappings $mappings -ReleaseDirectory $releaseDir
        Commit-ReleaseArtifacts -Mappings $mappings
        Write-Host "输入：$stageRoot；输出：$releaseDir；处理 4，跳过 0，失败 0。"
    }
    catch {
        Write-Error "发布失败。输入：$stageRoot；输出：$releaseDir；失败原因：$($_.Exception.Message)" -ErrorAction Continue
        throw
    }
    finally {
        if ($null -ne $publishLock) { $publishLock.Dispose() }
        Pop-Location
    }
}
if (-not $FunctionsOnly) { Invoke-Publish }
