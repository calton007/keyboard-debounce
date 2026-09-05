$ErrorActionPreference = 'Stop'
function Initialize-Toolchain {
    $cargoBin = Join-Path $env:USERPROFILE '.cargo\bin'
    if (Test-Path -LiteralPath (Join-Path $cargoBin 'cargo.exe')) { $env:PATH = "$cargoBin;$env:PATH" }
    foreach ($tool in @('cargo.exe', 'rustc.exe', 'npm.cmd', 'node.exe')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "缺少构建工具 $tool。请先安装 Node.js 22 LTS、Rust MSVC 和 C++ Build Tools。"
        }
    }
}
function Invoke-Checked {
    param([string] $Program, [string[]] $Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}
