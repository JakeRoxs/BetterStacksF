<#
Package script for BetterStacks
- builds the project (configurable)
- copies the built DLL and BetterStacksConfig.json into a `mods/` folder
- creates a versioned zip named `BetterStacks_v{version}.zip` under `dist/`

Usage:
  pwsh .\tools\package-mod.ps1                # Release build, create ZIP
  pwsh .\tools\package-mod.ps1 -Configuration Debug  # Debug build
  pwsh .\tools\package-mod.ps1 -Install        # also copy files into local Schedule I mods folder
  pwsh .\tools\package-mod.ps1 -NoBuild       # package existing build output

#>
param(
    [string]$Configuration = 'Release',
    [string]$ProjectFile = $null,
    [string]$AssemblyName = 'BetterStacks',
    [string]$OutputDir = $null,
    [string]$GameModsPath = 'I:\SteamLibrary\steamapps\common\Schedule I\mods',
    [switch]$Install,
    [switch]$NoBuild,
    [string]$VersionOverride = $null
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $ProjectFile) { $ProjectFile = Join-Path $scriptDir '..\BetterStacks\BetterStacks.csproj' }
if (-not $OutputDir) { $OutputDir = Join-Path $scriptDir '..\dist' }
$projectFile = Resolve-Path -Path $ProjectFile -ErrorAction SilentlyContinue
if (-not $projectFile) { Write-Error "Project file not found: $ProjectFile"; exit 2 }
$projectDir = Split-Path -Parent $projectFile

function Get-VersionFromSource {
    $classFile = Join-Path $projectDir 'Class1.cs'
    if (-not (Test-Path $classFile)) { return $null }
    $content = Get-Content $classFile -Raw
    $m = [regex]::Match($content, 'MelonInfo\([^)]*,\s*"[^"]*"\s*,\s*"(?<ver>[^"\)]+)"')
    if ($m.Success) { return $m.Groups['ver'].Value }
    return $null
}

function Get-VersionFromAssembly([string]$dllPath) {
    if (-not (Test-Path $dllPath)) { return $null }
    try {
        $asm = [System.Reflection.Assembly]::LoadFrom($dllPath)
        $attr = $asm.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -like 'MelonInfo*' } | Select-Object -First 1
        if ($attr) {
            return $attr.ConstructorArguments[2].Value -as [string]
        }
    } catch {
        Write-Warning "Unable to read assembly attributes: $_"
    }
    return $null
}

# 1) build (optional)
if (-not $NoBuild) {
    Write-Host "Building project ($Configuration)..."
    dotnet build $projectFile -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit $LASTEXITCODE }
}

$buildOutput = Join-Path $projectDir "bin\$Configuration\net6.0"
if (-not (Test-Path $buildOutput)) { Write-Error "Build output not found: $buildOutput"; exit 3 }
$dllPath = Join-Path $buildOutput "$AssemblyName.dll"
if (-not (Test-Path $dllPath)) { Write-Error "DLL not found: $dllPath"; exit 4 }

# 2) determine version
$version = $null
if ($VersionOverride) { $version = $VersionOverride }
else {
    $version = Get-VersionFromSource
    if (-not $version) { $version = Get-VersionFromAssembly -dllPath $dllPath }
}
if (-not $version) { Write-Error "Unable to determine mod version. Provide -VersionOverride or ensure MelonInfo is present in source."; exit 5 }

# 3) prepare package folder (clean previous outputs so stale files aren't included)
$packageRoot = Join-Path $OutputDir "$AssemblyName`_v$version"
if (Test-Path $packageRoot) { Remove-Item $packageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

# 4) copy files — for Nexus/Vortex packages the DLL must be inside a lowercase `mods/` folder
$modsDir = Join-Path $packageRoot 'mods'
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
Copy-Item -Path $dllPath -Destination (Join-Path $modsDir (Split-Path $dllPath -Leaf)) -Force
# NOTE: BetterStacksConfig.json is intentionally NOT included in the ZIP. The mod will
# auto-create a default config file in the game's `mods/` folder on first run so
# s1-modsapp (or the user) can edit it there. This avoids Vortex hard-link/install issues.

# Packaging minimal mod ZIP: include only `mods/BetterStacks.dll`. No README/icon/license are packaged.
$manifestSrc = Join-Path $projectDir 'manifest.json'
$hasManifest = Test-Path $manifestSrc
# NOTE: FOMOD intentionally excluded from the packaged ZIP — Vortex/Thunderstore will use the flat ZIP layout.

# 5) create main ZIP — include the package root contents so `Mods/` is present at archive root (Thunderstore-friendly)
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$zipName = "$AssemblyName`_v$version.zip"
$zipPath = Join-Path $OutputDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
# include the package root contents (this preserves the `Mods/` folder inside the ZIP)
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -Force

# Validate ZIP: ensure no top-level file named 'mods'/'Mods' and ensure DLL exists at archive root
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$entries = $zip.Entries | ForEach-Object { $_.FullName }
$zip.Dispose()

# Fail if a top-level file named 'mods' is present in the archive (prevents extractors from creating a file named 'mods')
if ($entries -contains 'mods') {
    Write-Error "Package validation failed: archive contains a file entry named 'mods' (will be interpreted as a file by some installers). Aborting packaging."
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    exit 6
}

# Ensure DLL exists under 'mods/' (lowercase) for the Nexus/Vortex package
if (-not ($entries -contains 'mods/BetterStacks.dll')) {
    Write-Error "Package validation failed: 'mods/BetterStacks.dll' not present in archive. Aborting packaging."
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    exit 7
} 
Write-Host "Main package created: $zipPath" -ForegroundColor Green
Write-Host "Package folder: $packageRoot"

# Create a Thunderstore-specific ZIP (includes manifest.json) with '-ts' suffix
if ($hasManifest) {
    $tsZipName = "$AssemblyName`_v$version-ts.zip"
    $tsZipPath = Join-Path $OutputDir $tsZipName
    if (Test-Path $tsZipPath) { Remove-Item $tsZipPath -Force }

    # temporarily add manifest.json into package root, create TS zip, then remove it
    Copy-Item -Path $manifestSrc -Destination (Join-Path $packageRoot 'manifest.json') -Force
    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $tsZipPath -Force

    # validate TS ZIP contains manifest.json
    $zip = [System.IO.Compression.ZipFile]::OpenRead($tsZipPath)
    $entries = $zip.Entries | ForEach-Object { $_.FullName }
    $zip.Dispose()
    if (-not ($entries -contains 'manifest.json')) {
        Write-Error "Thunderstore package validation failed: manifest.json missing from $tsZipPath"
        Remove-Item $tsZipPath -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $packageRoot 'manifest.json') -Force -ErrorAction SilentlyContinue
        exit 9
    }
    # cleanup
    Remove-Item -Path (Join-Path $packageRoot 'manifest.json') -Force -ErrorAction SilentlyContinue
    Write-Host "Thunderstore package created: $tsZipPath" -ForegroundColor Green
} else {
    Write-Host "No manifest.json found — Thunderstore (-ts) package not created." -ForegroundColor Yellow
}

# Single ZIP only — packaging places `BetterStacks.dll` at the ZIP root (Thunderstore/Vortex compatible).
# 6) optional install into local game mods folder
if ($Install) {
    if (Test-Path $GameModsPath) {
        # copy contents of packaged mods/ into the game's Mods folder
        Copy-Item -Path (Join-Path $packageRoot 'mods\*') -Destination $GameModsPath -Recurse -Force
        Write-Host "Installed mod files into: $GameModsPath" -ForegroundColor Green
    } else {
        Write-Warning "Game mods folder not found: $GameModsPath. Use -GameModsPath to override." }
}

exit 0
