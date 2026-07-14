[CmdletBinding()]
param(
    [switch]$SkipDotNetBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Common.ps1")

$repositoryRoot = Get-RepositoryRoot
$codeErrors = [System.Collections.Generic.List[string]]::new()
$environmentIssues = [System.Collections.Generic.List[string]]::new()
$notes = [System.Collections.Generic.List[string]]::new()

function Add-CodeError {
    param([string]$Message)
    $script:codeErrors.Add($Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Add-EnvironmentIssue {
    param([string]$Message)
    $script:environmentIssues.Add($Message)
    Write-Host "[ENV]   $Message" -ForegroundColor Yellow
}

function Add-Note {
    param([string]$Message)
    $script:notes.Add($Message)
    Write-Host "[OK]    $Message" -ForegroundColor Green
}

Write-Host "Win Mouse Fix validation" -ForegroundColor Cyan
Write-Host "Repository: $repositoryRoot"

$engineEntryPoint = Find-EngineEntryPoint -RepositoryRoot $repositoryRoot
if ($null -ne $engineEntryPoint) {
    $relativeEnginePath = Get-RelativePath -BasePath $repositoryRoot -Path $engineEntryPoint.FullName
    Add-Note "AutoHotkey entry point exists: $relativeEnginePath"
} else {
    Add-CodeError "Missing AutoHotkey entry point: expected src/engine/Engine.ahk or src/engine/MouseEngine.ahk."
}

$engineRoot = Join-Path $repositoryRoot "src\engine"
$engineFiles = @()
if (Test-Path -LiteralPath $engineRoot -PathType Container) {
    $engineFiles = @(Get-ChildItem -LiteralPath $engineRoot -Filter "*.ahk" -File -Recurse)
}

if ($engineFiles.Count -eq 0) {
    Add-CodeError "No AutoHotkey source files were found under src/engine."
} else {
    Add-Note "Found $($engineFiles.Count) AutoHotkey source file(s)."
}

$defaultConfig = Join-Path $repositoryRoot "config\default.json"
if (-not (Test-Path -LiteralPath $defaultConfig -PathType Leaf)) {
    Add-CodeError "Missing default configuration: config/default.json"
}

$ignoredDirectoryPattern = "[\\/](bin|obj|dist|\.git)[\\/]"
$jsonFiles = @(
    Get-ChildItem -LiteralPath $repositoryRoot -Filter "*.json" -File -Recurse |
        Where-Object { $_.FullName -notmatch $ignoredDirectoryPattern }
)

if ($jsonFiles.Count -eq 0) {
    Add-CodeError "No JSON files were found to validate."
} else {
    foreach ($jsonFile in $jsonFiles) {
        $relativePath = Get-RelativePath -BasePath $repositoryRoot -Path $jsonFile.FullName
        try {
            $content = Get-Content -LiteralPath $jsonFile.FullName -Raw
            if ([string]::IsNullOrWhiteSpace($content)) {
                throw "File is empty."
            }

            $null = $content | ConvertFrom-Json
            Add-Note "Valid JSON: $relativePath"
        } catch {
            Add-CodeError "Invalid JSON in ${relativePath}: $($_.Exception.Message)"
        }
    }
}

if (Test-Path -LiteralPath $defaultConfig -PathType Leaf) {
    try {
        $config = Get-Content -LiteralPath $defaultConfig -Raw | ConvertFrom-Json
        $configProperties = @($config.PSObject.Properties.Name)
        if ($config.configVersion -ne 2) {
            Add-CodeError "config/default.json must use configVersion 2."
        } elseif ($configProperties -contains "buttons") {
            Add-CodeError "config/default.json still contains the obsolete buttons field."
        } elseif (@("fast", "medium", "slow") -notcontains $config.doubleClickSpeed) {
            Add-CodeError "config/default.json has an invalid doubleClickSpeed value."
        } elseif ($config.desktopSwipeDirection -ne "followMouse") {
            Add-CodeError "config/default.json must default desktopSwipeDirection to followMouse."
        } elseif ($null -eq $config.remaps -or @($config.remaps).Count -eq 0) {
            Add-CodeError "config/default.json must contain recommended remaps."
        } else {
            $expectedRemaps = @(
                "MButton|click|Original",
                "MButton|holdScroll|FastScroll",
                "XButton2|click|Forward",
                "XButton2|holdScroll|Zoom",
                "XButton2|holdDrag|ScrollMove",
                "XButton1|click|Back",
                "XButton1|doubleClick|TaskView",
                "XButton1|holdDrag|DesktopNavigation"
            )
            $actualRemaps = @($config.remaps | ForEach-Object {
                "$($_.button)|$($_.trigger)|$($_.action.type)"
            })
            $missingRemaps = @($expectedRemaps | Where-Object { $actualRemaps -notcontains $_ })
            if ($missingRemaps.Count -gt 0) {
                Add-CodeError "config/default.json is missing recommended remaps: $($missingRemaps -join ', ')"
            } else {
                Add-Note "Default configuration uses configVersion 2 and contains all recommended remaps."
            }
        }
    } catch {
        Add-CodeError "Default configuration structure could not be validated: $($_.Exception.Message)"
    }
}

$configSchema = Join-Path $repositoryRoot "src\shared\config.schema.json"
if (Test-Path -LiteralPath $configSchema -PathType Leaf) {
    try {
        $schema = Get-Content -LiteralPath $configSchema -Raw | ConvertFrom-Json
        if ($schema.properties.configVersion.const -ne 2) {
            Add-CodeError "src/shared/config.schema.json must require configVersion 2."
        } elseif (@($schema.required) -notcontains "remaps" -or
                  @($schema.required) -notcontains "doubleClickSpeed" -or
                  @($schema.required) -notcontains "desktopSwipeDirection") {
            Add-CodeError "src/shared/config.schema.json must require remaps, doubleClickSpeed, and desktopSwipeDirection."
        } else {
            Add-Note "Configuration schema requires the version 2 fields."
        }
    } catch {
        Add-CodeError "Configuration schema structure could not be validated: $($_.Exception.Message)"
    }
}

$messageContract = Join-Path $repositoryRoot "src\shared\messages.json"
if (Test-Path -LiteralPath $messageContract -PathType Leaf) {
    try {
        $messages = Get-Content -LiteralPath $messageContract -Raw | ConvertFrom-Json
        if ($messages.configuration.transport -ne "file" -or $messages.configuration.configVersion -ne 2) {
            Add-CodeError "src/shared/messages.json must describe the version 2 file configuration transport."
        } elseif ($messages.control.transport -ne "windowsMessage" -or
                  $messages.control.messages.reloadConfig -ne "0x8001" -or
                  $messages.control.messages.pause -ne "0x8002" -or
                  $messages.control.messages.resume -ne "0x8003") {
            Add-CodeError "src/shared/messages.json has incorrect Windows message values."
        } else {
            Add-Note "GUI and engine communication metadata matches the current implementation."
        }
    } catch {
        Add-CodeError "GUI and engine communication metadata could not be validated: $($_.Exception.Message)"
    }
}

$guiProject = Find-GuiProject -RepositoryRoot $repositoryRoot
if ($null -eq $guiProject) {
    Add-CodeError "Expected one .NET project under src/gui, preferably src/gui/WinMouseFix.Gui.csproj."
} else {
    $relativeProjectPath = Get-RelativePath -BasePath $repositoryRoot -Path $guiProject.FullName
    Add-Note ".NET project exists: $relativeProjectPath"
    $guiTargetFramework = Get-DotNetProjectTargetFramework -Project $guiProject
    if ($guiTargetFramework -ne "net48") {
        Add-CodeError "GUI project must target .NET Framework 4.8 (net48), but targets '$guiTargetFramework'."
    } else {
        Add-Note "GUI project targets .NET Framework 4.8."
    }
}

if (-not (Test-NetFramework48Installed)) {
    Add-EnvironmentIssue ".NET Framework 4.8 runtime is not installed."
} else {
    Add-Note ".NET Framework 4.8 runtime is installed."
}

$dotnetPath = Find-CommandPath -Names @("dotnet")
if ($null -eq $dotnetPath) {
    Add-EnvironmentIssue ".NET SDK is not installed or dotnet is not available on PATH. Install the SDK version required by the GUI project."
} elseif ($null -ne $guiProject -and -not $SkipDotNetBuild) {
    Write-Host "[RUN]   dotnet build $relativeProjectPath" -ForegroundColor Cyan
    & $dotnetPath build $guiProject.FullName --configuration Debug --nologo
    $buildExitCode = $LASTEXITCODE
    if ($buildExitCode -ne 0) {
        Add-CodeError ".NET build failed with exit code $buildExitCode."
    } else {
        Add-Note ".NET project builds successfully."

        $guiExecutable = Get-DotNetProjectExecutablePath -Project $guiProject -Configuration "Debug"
        Write-Host "[RUN]   $guiExecutable --validate-ui" -ForegroundColor Cyan
        if ($null -eq $guiExecutable -or -not (Test-Path -LiteralPath $guiExecutable -PathType Leaf)) {
            Add-CodeError "GUI executable was not produced at the expected .NET Framework output path."
        } else {
            $guiValidation = Start-Process `
                -FilePath $guiExecutable `
                -ArgumentList "--validate-ui" `
                -WindowStyle Hidden `
                -PassThru `
                -Wait
            if ($guiValidation.ExitCode -ne 0) {
                Add-CodeError "GUI auxiliary-window check failed with exit code $($guiValidation.ExitCode)."
            } else {
                Add-Note "GUI auxiliary windows load successfully."
            }
        }

        $configCheckProject = Join-Path $repositoryRoot "tests\config\WinMouseFix.ConfigCheck.csproj"
        if (Test-Path -LiteralPath $configCheckProject -PathType Leaf) {
            $configCheckProjectInfo = [System.IO.FileInfo]::new($configCheckProject)
            Write-Host "[RUN]   dotnet build tests/config/WinMouseFix.ConfigCheck.csproj" -ForegroundColor Cyan
            & $dotnetPath build $configCheckProject --configuration Debug --nologo
            $configBuildExitCode = $LASTEXITCODE
            if ($configBuildExitCode -ne 0) {
                Add-CodeError "Configuration check build failed with exit code $configBuildExitCode."
            } else {
                $configCheckExecutable = Get-DotNetProjectExecutablePath -Project $configCheckProjectInfo -Configuration "Debug"
                Write-Host "[RUN]   $configCheckExecutable" -ForegroundColor Cyan
                if ($null -eq $configCheckExecutable -or -not (Test-Path -LiteralPath $configCheckExecutable -PathType Leaf)) {
                    Add-CodeError "Configuration check executable was not produced at the expected .NET Framework output path."
                } else {
                    & $configCheckExecutable
                    if ($LASTEXITCODE -ne 0) {
                        Add-CodeError "Configuration migration check failed with exit code $LASTEXITCODE."
                    } else {
                        Add-Note "Configuration migration and recommended settings check passed."
                    }
                }
            }
        }
    }
} elseif ($SkipDotNetBuild) {
    Add-Note ".NET build was skipped by request."
}

$autoHotkeyPath = Find-AutoHotkeyRuntime -RepositoryRoot $repositoryRoot
if ($null -eq $autoHotkeyPath) {
    Add-EnvironmentIssue "AutoHotkey v2 runtime was not found. Set AUTOHOTKEY_EXE or install AutoHotkey v2 to run the mouse engine during development."
} else {
    Add-Note "AutoHotkey runtime found: $autoHotkeyPath"

    if (Test-Path -LiteralPath $engineEntryPoint -PathType Leaf) {
        $validationOutput = Join-Path ([System.IO.Path]::GetTempPath()) ("winmousefix-ahk-out-" + [guid]::NewGuid() + ".txt")
        $validationError = Join-Path ([System.IO.Path]::GetTempPath()) ("winmousefix-ahk-err-" + [guid]::NewGuid() + ".txt")
        try {
            $quotedEnginePath = '"' + $engineEntryPoint.FullName.Replace('"', '\"') + '"'
            $engineValidation = Start-Process `
                -FilePath $autoHotkeyPath `
                -ArgumentList @("/ErrorStdOut", $quotedEnginePath, "--validate") `
                -RedirectStandardOutput $validationOutput `
                -RedirectStandardError $validationError `
                -WindowStyle Hidden `
                -PassThru `
                -Wait

            $engineOutput = Get-Content -LiteralPath $validationOutput -Raw -ErrorAction SilentlyContinue
            $engineError = Get-Content -LiteralPath $validationError -Raw -ErrorAction SilentlyContinue
            $engineOutput = if ($null -eq $engineOutput) { "" } else { ([string]$engineOutput).Trim() }
            $engineError = if ($null -eq $engineError) { "" } else { ([string]$engineError).Trim() }
            if ($engineValidation.ExitCode -ne 0) {
                Add-CodeError "AutoHotkey self-check failed with exit code $($engineValidation.ExitCode): $engineError"
            } elseif ($engineOutput -ne "") {
                Add-Note $engineOutput
            } else {
                Add-Note "AutoHotkey self-check passed."
            }
        } catch {
            Add-CodeError "AutoHotkey self-check could not run: $($_.Exception.Message)"
        } finally {
            Remove-Item -LiteralPath $validationOutput, $validationError -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ""
Write-Host "Summary: $($codeErrors.Count) code error(s), $($environmentIssues.Count) environment issue(s), $($notes.Count) successful check(s)."

if ($codeErrors.Count -gt 0) {
    Write-Host "Validation failed because repository content needs correction." -ForegroundColor Red
    exit 1
}

if ($environmentIssues.Count -gt 0) {
    Write-Host "Repository checks passed, but this computer is missing required development tools." -ForegroundColor Yellow
    exit 2
}

Write-Host "Validation passed." -ForegroundColor Green
exit 0
