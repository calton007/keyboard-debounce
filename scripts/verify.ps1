[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'toolchain.ps1')
Initialize-Toolchain
Push-Location $repositoryRoot
try {
    Invoke-Checked 'npm.cmd' @('ci', '--no-audit', '--no-fund')
    Invoke-Checked 'cargo.exe' @('fmt', '--manifest-path', 'src-tauri/Cargo.toml', '--check')
    Invoke-Checked 'cargo.exe' @('clippy', '--manifest-path', 'src-tauri/Cargo.toml', '--locked', '--all-targets', '--', '-D', 'warnings')
    Invoke-Checked 'cargo.exe' @('test', '--manifest-path', 'src-tauri/Cargo.toml', '--locked')
    Invoke-Checked 'npm.cmd' @('run', 'typecheck')
    Invoke-Checked 'npm.cmd' @('test')
    Invoke-Checked 'npm.cmd' @('run', 'build')
    & (Join-Path $repositoryRoot 'tests\PublishScript.Tests.ps1')
    if (-not $?) { throw '发布契约测试失败' }
    Write-Host 'Verification completed successfully.'
}
finally { Pop-Location }
