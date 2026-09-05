# JellyVote

Community voting to delete movies, series, and music from your Jellyfin library.

Users propose a deletion with a preset reason. Other eligible users are notified and vote **Yes** or **No** (with a keep reason). Quorum adapts to how many active users can see the item, so small households are not blocked by an unreachable minimum.

## Features

- Propose delete from item detail pages (jellyfin-web)
- Preset delete / keep reasons (editable in settings)
- Session messages + in-app vote inbox
- Adaptive quorum: `requiredYes = min(configuredMinimum, eligibleActiveUsers)`
- Early resolve when quorum is met; optional unanimous pass at window end
- Deletion mode setting: **library only** (default) or **delete files from disk**
- Optional admin approval before disk deletes
- Dry-run mode, playing-item guard, audit log
- Multi-tab admin settings UI (General, Voting, Deletion, Reasons, Notifications, Permissions, Votes, About)

## Requirements

- Jellyfin **10.11.x**
- jellyfin-web (client UI is injected into the web client)

## Install from repository

1. Dashboard → Plugins → Repositories → add:
   `https://raw.githubusercontent.com/ThuGie/jellyfin-plugin-jellyvote/main/manifest.json`
2. Catalog → find **JellyVote** → Install
3. Restart Jellyfin
4. Configure under Dashboard → Plugins → JellyVote

## Manual install

1. Download the release zip
2. Extract into your Jellyfin `plugins/JellyVote/` folder
3. Restart Jellyfin

## Build

```bash
dotnet build Jellyfin.Plugin.JellyVote/Jellyfin.Plugin.JellyVote.csproj -c Release
```

Packaging helper (PowerShell):

```powershell
./build.ps1                  # build zip + print MD5
./build.ps1 -UpdateManifest  # also write checksum into manifest.json
```

### Manifest checksums

On every **version tag** push (`v*`), GitHub Actions:

1. Builds the plugin zip (DLL + `meta.json` + `thumb.png`)
2. Computes the **MD5** checksum
3. Creates the GitHub Release with the zip
4. Checks out `main` and runs `scripts/update-manifest.ps1` to prepend the new version entry (with correct `checksum` + `sourceUrl`) and push

You do **not** need to hand-edit the hash for tagged releases.

## Icon

- Catalog: [`docs/images/icon.png`](docs/images/icon.png) (512×512) via `imageUrl` in `manifest.json`
- Installed plugin: `thumb.png` (256×256) inside the zip (`imagePath` in `meta.json`)

## Safety defaults

- Remove from library only (disk delete off)
- Admin approval required when disk delete is enabled
- Never delete while the item is actively playing
- Audit trail written to the plugin data folder (`votes.json`, `audit.log`)

## License

GPL-3.0 (same family as Jellyfin plugins)
