param(
  [Parameter(Mandatory = $true)][string]$Version,
  [Parameter(Mandatory = $true)][string]$Checksum,
  [string]$Repository = $env:GITHUB_REPOSITORY,
  [string]$TargetAbi = "10.11.0.0",
  [string]$Changelog = "",
  [string]$ManifestPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Repository)) {
  $Repository = "ThuGie/jellyfin-plugin-jellyvote"
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
  $root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
  $ManifestPath = Join-Path $root "manifest.json"
  if (-not (Test-Path $ManifestPath)) {
    $ManifestPath = Join-Path (Get-Location) "manifest.json"
  }
}

$Checksum = $Checksum.Trim().ToUpperInvariant()
$Version = $Version.Trim().TrimStart("v")

if ([string]::IsNullOrWhiteSpace($Changelog)) {
  $Changelog = "Release $Version - see GitHub release notes."
}

$raw = [System.IO.File]::ReadAllText($ManifestPath)
$manifest = $raw | ConvertFrom-Json
if ($manifest -isnot [System.Array]) {
  $manifest = @($manifest)
}

$pkg = $manifest[0]
$pkg.imageUrl = "https://raw.githubusercontent.com/$Repository/main/docs/images/icon.png"

$existing = @()
if ($pkg.versions) {
  foreach ($v in @($pkg.versions)) {
    if ($v.version -eq $Version) { continue }
    $existing += [ordered]@{
      version = [string]$v.version
      changelog = [string]$v.changelog
      targetAbi = [string]$v.targetAbi
      sourceUrl = [string]$v.sourceUrl
      checksum = [string]$v.checksum
      timestamp = [string]$v.timestamp
    }
  }
}

$versions = @([ordered]@{
  version = $Version
  changelog = $Changelog
  targetAbi = $TargetAbi
  sourceUrl = "https://github.com/$Repository/releases/download/v$Version/Jellyfin.Plugin.JellyVote_$Version.zip"
  checksum = $Checksum
  timestamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
}) + $existing

$outObj = @([ordered]@{
  guid = [string]$pkg.guid
  name = [string]$pkg.name
  description = [string]$pkg.description
  overview = [string]$pkg.overview
  owner = [string]$pkg.owner
  category = [string]$pkg.category
  imageUrl = [string]$pkg.imageUrl
  versions = $versions
})

$json = ConvertTo-Json -InputObject $outObj -Depth 20
[System.IO.File]::WriteAllText((Resolve-Path $ManifestPath), $json.TrimEnd() + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated $ManifestPath for version $Version (checksum $Checksum)"