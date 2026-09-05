param(
  [string]$Configuration = "Release",
  [string]$Version = "1.0.0.0"
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

$meta = Get-Content (Join-Path $root "build\meta.json") -Raw | ConvertFrom-Json
$meta.version = $Version
$meta.timestamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$meta | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $outDir "meta.json") -Encoding UTF8

$zip = Join-Path $root "dist\Jellyfin.Plugin.JellyVote_$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force

Write-Host "Built $zip"
Get-FileHash $zip -Algorithm MD5 | Format-List
