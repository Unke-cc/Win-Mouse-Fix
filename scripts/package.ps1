[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$installerScript = Join-Path $repositoryRoot "installer\WinMouseFix.iss"
$expectedOutput = Join-Path $repositoryRoot "dist\WinMouseFix-Setup-0.1.0.exe"

& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$compilerCandidates = @(
    $env:INNO_SETUP_COMPILER,
    (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$compiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1

if ($null -eq $compiler) {
    Write-Host "[ENV] Inno Setup 6 compiler was not found. Set INNO_SETUP_COMPILER or install Inno Setup 6." -ForegroundColor Yellow
    exit 2
}

& $compiler $installerScript
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $expectedOutput -PathType Leaf)) {
    Write-Host "[ERROR] Installer output was not created: $expectedOutput" -ForegroundColor Red
    exit 1
}

Write-Host "Package completed: $expectedOutput" -ForegroundColor Green
