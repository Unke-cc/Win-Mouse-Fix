Set-StrictMode -Version Latest

function Get-RepositoryRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullBasePath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $baseUri = [System.Uri]::new($fullBasePath)
    $pathUri = [System.Uri]::new($fullPath)
    $relativeUri = $baseUri.MakeRelativeUri($pathUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace(
        [System.IO.Path]::AltDirectorySeparatorChar,
        [System.IO.Path]::DirectorySeparatorChar
    )
}

function Find-CommandPath {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            return $command.Source
        }
    }

    return $null
}

function Find-AutoHotkeyRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:AUTOHOTKEY_EXE)) {
        $candidates.Add($env:AUTOHOTKEY_EXE)
    }

    $candidates.Add((Join-Path $RepositoryRoot "tools\autohotkey\AutoHotkey64.exe"))
    $candidates.Add((Join-Path $RepositoryRoot "tools\autohotkey\AutoHotkey.exe"))

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates.Add((Join-Path $env:ProgramFiles "AutoHotkey\v2\AutoHotkey64.exe"))
        $candidates.Add((Join-Path $env:ProgramFiles "AutoHotkey\AutoHotkey.exe"))
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates.Add((Join-Path $env:LOCALAPPDATA "Programs\AutoHotkey\v2\AutoHotkey64.exe"))
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    return Find-CommandPath -Names @("AutoHotkey64.exe", "AutoHotkey.exe", "AutoHotkey")
}

function Find-Ahk2ExeCompiler {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:AHK2EXE_EXE)) {
        $candidates.Add($env:AHK2EXE_EXE)
    }

    $candidates.Add((Join-Path $RepositoryRoot ".tools\Ahk2Exe\Ahk2Exe.exe"))
    $candidates.Add((Join-Path $RepositoryRoot "tools\autohotkey\Compiler\Ahk2Exe.exe"))

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates.Add((Join-Path $env:ProgramFiles "AutoHotkey\Compiler\Ahk2Exe.exe"))
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates.Add((Join-Path $env:LOCALAPPDATA "Programs\AutoHotkey\Compiler\Ahk2Exe.exe"))
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    return Find-CommandPath -Names @("Ahk2Exe.exe", "Ahk2Exe")
}

function Find-GuiProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $expectedProject = Join-Path $RepositoryRoot "src\gui\WinMouseFix.Gui.csproj"
    if (Test-Path -LiteralPath $expectedProject -PathType Leaf) {
        return [System.IO.FileInfo]::new($expectedProject)
    }

    $guiRoot = Join-Path $RepositoryRoot "src\gui"
    if (-not (Test-Path -LiteralPath $guiRoot -PathType Container)) {
        return $null
    }

    $projects = @(Get-ChildItem -LiteralPath $guiRoot -Filter "*.csproj" -File -Recurse)
    if ($projects.Count -eq 1) {
        return $projects[0]
    }

    return $null
}

function Get-DotNetProjectTargetFramework {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Project
    )

    [xml]$projectDocument = Get-Content -LiteralPath $Project.FullName -Raw
    $targetFrameworkNode = $projectDocument.SelectSingleNode("/Project/PropertyGroup/TargetFramework")
    if ($null -eq $targetFrameworkNode) {
        return $null
    }

    return $targetFrameworkNode.InnerText.Trim()
}

function Get-DotNetProjectExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Project,

        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    [xml]$projectDocument = Get-Content -LiteralPath $Project.FullName -Raw
    $assemblyNameNode = $projectDocument.SelectSingleNode("/Project/PropertyGroup/AssemblyName")
    $assemblyName = if ($null -eq $assemblyNameNode) { $Project.BaseName } else { $assemblyNameNode.InnerText.Trim() }
    $targetFramework = Get-DotNetProjectTargetFramework -Project $Project
    if ([string]::IsNullOrWhiteSpace($targetFramework)) {
        return $null
    }

    return Join-Path $Project.Directory.FullName "bin\$Configuration\$targetFramework\$assemblyName.exe"
}

function Test-NetFramework48Installed {
    $releaseKey = Get-ItemPropertyValue `
        -LiteralPath "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" `
        -Name Release `
        -ErrorAction SilentlyContinue
    return $null -ne $releaseKey -and [int]$releaseKey -ge 528040
}

function Find-EngineEntryPoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    foreach ($relativePath in @("src\engine\Engine.ahk", "src\engine\MouseEngine.ahk")) {
        $candidate = Join-Path $RepositoryRoot $relativePath
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [System.IO.FileInfo]::new($candidate)
        }
    }

    return $null
}

function Test-PathWithinRepository {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    $requiredPrefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar
    return $fullPath.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}
