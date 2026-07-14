[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Common.ps1")

$repositoryRoot = Get-RepositoryRoot
$engineEntryPoint = Find-EngineEntryPoint -RepositoryRoot $repositoryRoot
$guiProject = Find-GuiProject -RepositoryRoot $repositoryRoot
$dotnetPath = Find-CommandPath -Names @("dotnet")
$autoHotkeyPath = Find-AutoHotkeyRuntime -RepositoryRoot $repositoryRoot
$codeErrors = [System.Collections.Generic.List[string]]::new()
$environmentIssues = [System.Collections.Generic.List[string]]::new()

if ($null -eq $engineEntryPoint) {
    $codeErrors.Add("Missing src/engine/Engine.ahk or src/engine/MouseEngine.ahk.")
}

if ($null -eq $guiProject) {
    $codeErrors.Add("Expected one .NET project under src/gui.")
} elseif ((Get-DotNetProjectTargetFramework -Project $guiProject) -ne "net48") {
    $codeErrors.Add("The GUI project must target .NET Framework 4.8 (net48).")
}

if ($null -eq $dotnetPath) {
    $environmentIssues.Add("dotnet was not found. Install the required .NET SDK.")
}

if ($null -eq $autoHotkeyPath) {
    $environmentIssues.Add("AutoHotkey v2 was not found. Set AUTOHOTKEY_EXE or install AutoHotkey v2.")
}

if (-not (Test-NetFramework48Installed)) {
    $environmentIssues.Add(".NET Framework 4.8 runtime is not installed.")
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

$engineProcess = $null
try {
    $relativeProjectPath = Get-RelativePath -BasePath $repositoryRoot -Path $guiProject.FullName
    Write-Host "Building $relativeProjectPath for .NET Framework 4.8..." -ForegroundColor Cyan
    & $dotnetPath build $guiProject.FullName --configuration Debug --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "GUI build failed with exit code $LASTEXITCODE." -ForegroundColor Red
        exit 1
    }

    $guiExecutable = Get-DotNetProjectExecutablePath -Project $guiProject -Configuration "Debug"
    if ($null -eq $guiExecutable -or -not (Test-Path -LiteralPath $guiExecutable -PathType Leaf)) {
        Write-Host "GUI executable was not produced at the expected .NET Framework output path." -ForegroundColor Red
        exit 1
    }

    Write-Host "Starting AutoHotkey mouse engine..." -ForegroundColor Cyan
    $quotedEnginePath = '"' + $engineEntryPoint.FullName.Replace('"', '\"') + '"'
    $engineProcess = Start-Process -FilePath $autoHotkeyPath -ArgumentList @("/ErrorStdOut", $quotedEnginePath) -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 400

    if ($engineProcess.HasExited -and $engineProcess.ExitCode -ne 0) {
        Write-Host "AutoHotkey mouse engine stopped with exit code $($engineProcess.ExitCode)." -ForegroundColor Red
        exit 1
    }

    Write-Host "Starting GUI from $guiExecutable..." -ForegroundColor Cyan
    $guiProcess = Start-Process -FilePath $guiExecutable -PassThru -Wait
    if ($guiProcess.ExitCode -ne 0) {
        Write-Host "GUI stopped with exit code $($guiProcess.ExitCode)." -ForegroundColor Red
        exit 1
    }
} finally {
    if ($null -ne $engineProcess -and -not $engineProcess.HasExited) {
        Write-Host "Stopping development mouse engine..." -ForegroundColor Cyan
        Stop-Process -Id $engineProcess.Id -ErrorAction SilentlyContinue
    }
}

exit 0
