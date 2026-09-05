param(
  [string]$Configuration = "Release",
  [string]$Version = "1.0.0.0",
  [switch]$UpdateManifest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "Jellyfin.Plugin.JellyVote\Jellyfin.Plugin.JellyVote.csproj"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

& $dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$outDir = Join-Path $root "dist\JellyVote"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item (Join-Path $root "Jellyfin.Plugin.JellyVote\bin\$Configuration\net9.0\Jellyfin.Plugin.JellyVote.dll") $outDir -Force

$thumbSrc = Join-Path $root "build\thumb.png"
if (Test-Path $thumbSrc) {
  Copy-Item $thumbSrc (Join-Path $outDir "thumb.png") -Force
}

$meta = Get-Content (Join-Path $root "build\meta.json") -Raw | ConvertFrom-Json
$meta.version = $Version
$meta.timestamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$meta.imagePath = "thumb.png"
$metaJson = $meta | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $outDir "meta.json"), $metaJson + "`n", [System.Text.UTF8Encoding]::new($false))

$zip = Join-Path $root "dist\Jellyfin.Plugin.JellyVote_$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force

$hash = (Get-FileHash $zip -Algorithm MD5).Hash.ToUpperInvariant()
Write-Host "Built $zip"
Write-Host "MD5 $hash"

if ($UpdateManifest) {
  & (Join-Path $root "scripts\update-manifest.ps1") `
    -Version $Version `
    -Checksum $hash `
    -Repository "ThuGie/jellyfin-plugin-jellyvote"
}
