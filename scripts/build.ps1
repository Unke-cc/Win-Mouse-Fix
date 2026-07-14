[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Common.ps1")

$repositoryRoot = Get-RepositoryRoot
$guiProject = Find-GuiProject -RepositoryRoot $repositoryRoot
$engineEntryPoint = Find-EngineEntryPoint -RepositoryRoot $repositoryRoot
$defaultConfig = Join-Path $repositoryRoot "config\default.json"
$dotnetPath = Find-CommandPath -Names @("dotnet")
$compilerPath = Find-Ahk2ExeCompiler -RepositoryRoot $repositoryRoot
$autoHotkeyPath = Find-AutoHotkeyRuntime -RepositoryRoot $repositoryRoot
$codeErrors = [System.Collections.Generic.List[string]]::new()
$environmentIssues = [System.Collections.Generic.List[string]]::new()

if ($null -eq $guiProject) {
    $codeErrors.Add("Expected one .NET project under src/gui.")
}

if ($null -eq $engineEntryPoint) {
    $codeErrors.Add("Missing src/engine/Engine.ahk or src/engine/MouseEngine.ahk.")
}

if (-not (Test-Path -LiteralPath $defaultConfig -PathType Leaf)) {
    $codeErrors.Add("Missing config/default.json.")
}

if ($null -eq $dotnetPath) {
    $environmentIssues.Add("dotnet was not found. Install the required .NET SDK.")
}

if ($null -eq $compilerPath) {
    $environmentIssues.Add("Ahk2Exe was not found. Set AHK2EXE_EXE or install the AutoHotkey v2 compiler.")
}

foreach ($message in $codeErrors) {
    Write-Host "[ERROR] $message" -ForegroundColor Red
}

foreach ($message in $environmentIssues) {
    Write-Host "[ENV]   $message" -ForegroundColor Yellow
}

if ($codeErrors.Count -gt 0) {
    exit 1
}

if ($environmentIssues.Count -gt 0) {
    exit 2
}

$distRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "dist"))
$appOutput = Join-Path $distRoot "app"
if (-not (Test-PathWithinRepository -Path $distRoot -RepositoryRoot $repositoryRoot)) {
    Write-Host "[ERROR] Refusing to write outside the repository: $distRoot" -ForegroundColor Red
    exit 1
}

if (Test-Path -LiteralPath $appOutput) {
    Remove-Item -LiteralPath $appOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $appOutput -Force | Out-Null

Write-Host "Publishing .NET GUI for $RuntimeIdentifier..." -ForegroundColor Cyan
& $dotnetPath publish $guiProject.FullName `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $appOutput `
    --nologo `
    -p:PublishSingleFile=true `
    -p:DebugType=None
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    Write-Host "[ERROR] .NET publish failed with exit code $publishExitCode." -ForegroundColor Red
    exit 1
}

$engineOutput = Join-Path $appOutput "WinMouseFix.Engine.exe"
$quotedEngineInput = '"' + $engineEntryPoint.FullName.Replace('"', '\"') + '"'
$quotedEngineOutput = '"' + $engineOutput.Replace('"', '\"') + '"'
$compilerArguments = @("/silent", "verbose", "/in", $quotedEngineInput, "/out", $quotedEngineOutput)
if ($null -ne $autoHotkeyPath) {
    $quotedBasePath = '"' + $autoHotkeyPath.Replace('"', '\"') + '"'
    $compilerArguments += @("/base", $quotedBasePath)
}

Write-Host "Compiling AutoHotkey mouse engine..." -ForegroundColor Cyan
$compilerProcess = Start-Process `
    -FilePath $compilerPath `
    -ArgumentList $compilerArguments `
    -Wait `
    -PassThru
$compilerExitCode = $compilerProcess.ExitCode
if ($compilerExitCode -ne 0 -or -not (Test-Path -LiteralPath $engineOutput -PathType Leaf)) {
    Write-Host "[ERROR] AutoHotkey compilation failed with exit code $compilerExitCode." -ForegroundColor Red
    exit 1
}

$configOutput = Join-Path $appOutput "config"
New-Item -ItemType Directory -Path $configOutput -Force | Out-Null
Copy-Item -LiteralPath $defaultConfig -Destination (Join-Path $configOutput "default.json") -Force

$assetsRoot = Join-Path $repositoryRoot "assets"
if (Test-Path -LiteralPath $assetsRoot -PathType Container) {
    Copy-Item -LiteralPath $assetsRoot -Destination (Join-Path $appOutput "assets") -Recurse -Force
}

Write-Host "Build completed: $appOutput" -ForegroundColor Green
exit 0
