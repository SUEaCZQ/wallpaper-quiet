[CmdletBinding()]
param([string]$Ref = 'HEAD')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $commit = & git rev-parse --verify "$Ref^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Source reference must resolve to a Git commit.' }
    [xml]$project = (& git show "${commit}:WallpaperQuiet.csproj") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the project version from the commit.' }
    $version = [string]$project.Project.PropertyGroup.Version
    $files = & git ls-tree -r --name-only $commit
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read source manifest.' }
    $forbidden = $files | Where-Object {
        $_ -match '(^|/)(bin|obj|artifacts|validation|运行程序|旧版备份[^/]*|\.vs|\.idea)(/|$)' -or
        $_ -match '(?i)(^|/)(settings\.json|startup\.log|\.env(?:\..*)?|id_rsa|id_ed25519)$' -or
        $_ -match '(?i)\.(lnk|pdb|pfx|key|log|user|suo)$'
    }
    if ($forbidden) { throw "Private or generated files found: $($forbidden -join ', ')" }
    $release = Join-Path $root 'artifacts\release'
    New-Item -ItemType Directory -Force -Path $release | Out-Null
    $zip = Join-Path $release "wallpaper-quiet-$version-source.zip"
    & git archive --format=zip "--prefix=wallpaper-quiet-$version/" "--output=$zip" $commit
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    $installer = Join-Path $release "WallpaperQuiet-$version-Setup-x64.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Build the installer before generating checksums.' }
    $sums = foreach ($path in @($installer, $zip)) {
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path $path -Leaf)"
    }
    [IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
    Write-Output "Source commit: $commit"
    $sums
} finally { Pop-Location }
