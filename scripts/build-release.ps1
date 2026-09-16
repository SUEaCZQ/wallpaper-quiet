[CmdletBinding()]
param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'WallpaperQuiet.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if (-not $IsccPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
    else { $IsccPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $IsccPath)) { throw 'Install Inno Setup or pass -IsccPath.' }
$publish = Join-Path $root 'artifacts\publish\win-x64'
$release = Join-Path $root 'artifacts\release'
New-Item -ItemType Directory -Force -Path $release | Out-Null
Push-Location $root
try {
    # Self-contained output needs no preinstalled .NET runtime. Omit local PDB paths.
    & dotnet publish WallpaperQuiet.csproj -c Release -r win-x64 --self-contained true `
        -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    & $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DReleaseDir=$release" `
        (Join-Path $root 'packaging\WallpaperQuiet.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Get-Item -LiteralPath (Join-Path $release "WallpaperQuiet-$version-Setup-x64.exe")
} finally { Pop-Location }
