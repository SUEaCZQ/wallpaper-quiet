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
    # Preserve the license and third-party notices from the bundled runtime packs.
    $assets = Get-Content (Join-Path $root 'obj\project.assets.json') -Raw | ConvertFrom-Json
    $runtimeConfig = Get-Content (Join-Path $publish 'WallpaperQuiet.runtimeconfig.json') -Raw | ConvertFrom-Json
    $licenseDir = Join-Path $publish 'licenses'
    New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
    foreach ($pack in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $frameworkName = if ($pack.Contains('windowsdesktop')) { 'Microsoft.WindowsDesktop.App' } else { 'Microsoft.NETCore.App' }
        $framework = $runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $frameworkName }
        if (-not $framework) { throw "Bundled framework metadata is missing: $frameworkName" }
        $packageDir = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder ($pack + '/' + $framework.version)
            if (Test-Path -LiteralPath $candidate) { $packageDir = $candidate; break }
        }
        if (-not $packageDir) { throw "Runtime package is missing: $pack" }
        $notices = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE(?:\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
        if (-not $notices) { throw "Runtime license is missing: $pack" }
        foreach ($notice in $notices) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseDir ($pack + '-' + $notice.Name))
        }
    }
    & $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DReleaseDir=$release" `
        (Join-Path $root 'packaging\WallpaperQuiet.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Get-Item -LiteralPath (Join-Path $release "WallpaperQuiet-$version-Setup-x64.exe")
} finally { Pop-Location }
