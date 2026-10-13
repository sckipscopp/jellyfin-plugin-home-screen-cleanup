# Install, verify and roll back — server runbook

Target: Jellyfin **12.2.x** on Windows, installed as the `JellyfinServer` service running under a
non-admin service account, data folder `C:\ProgramData\Jellyfin\Server`. Run the checks from any
machine that can reach the server; installing is done in the Dashboard. Linux/Docker installs
differ only in paths (`/var/lib/jellyfin/plugins`, `/config/plugins`).

API calls below use an API key (Dashboard → API Keys) through the `Authorization` header:

```powershell
$base = 'http://<server>:8096'
$h    = @{ Authorization = 'MediaBrowser Client="runbook", Device="pc", DeviceId="runbook", Version="1", Token="<api key>"' }
```

## Before you start

| Item | Value |
|---|---|
| Repository URL | `https://raw.githubusercontent.com/sckipscopp/jellyfin-plugin-home-screen-cleanup/main/manifest.json` |
| Install folder | `C:\ProgramData\Jellyfin\Server\plugins\Home Screen Cleanup_1.0.2.0` |
| Data folder (hidden list) | `C:\ProgramData\Jellyfin\Server\plugins\Jellyfin.Plugin.HomeScreenCleanup\hidden-series.json` |
| Config file | `C:\ProgramData\Jellyfin\Server\plugins\configurations\Jellyfin.Plugin.HomeScreenCleanup.xml` |
| Downtime | One server restart (drops active streams). |

> **Note:** check for active streams first: `(Invoke-RestMethod "$base/Sessions" -Headers $h | ? NowPlayingItem).Count`.

## Phase 1 — Install from the repository

1. Dashboard → **Plugins** → **Repositories** → **+** → name `Home Screen Cleanup`, URL from the table.
   Raw GitHub is cached ~5 min; a release published moments ago may not show yet.
2. **Catalog** → *Home Screen Cleanup* → **Install** (version `1.0.2.0`).
3. Restart the server (Dashboard → **Restart**).
4. Optional: on the plugin's card, turn off auto-update so updates are deliberate.

**Verify:** Dashboard → Plugins shows *Home Screen Cleanup 1.0.2.0* as **Active**, or
`Invoke-RestMethod "$base/Plugins" -Headers $h | ? Name -eq 'Home Screen Cleanup' | select Version, Status`.
`NotSupported` = ABI / type-load problem (wrong server version); `Malfunctioned` = DI, constructor
or guid problem — read the log (Phase 3, step 6) and roll back (Phase 4).

## Phase 2 — HTTP checks

```powershell
$r = Invoke-WebRequest "$base/web/index.html" -Headers @{ 'Accept-Encoding' = 'gzip, br' }
([regex]::Matches($r.Content, 'data-hsc-injected')).Count     # 1
$r.Headers['Content-Encoding']                                # empty
(Invoke-WebRequest "$base/HomeScreenCleanup/client.js").Headers['Content-Type']   # application/javascript; charset=utf-8
(Invoke-WebRequest "$base/web/" -Headers @{ 'If-None-Match' = '"stale"' }).StatusCode   # 200, not 304
(Invoke-RestMethod "$base/Shows/NextUp?userId=<id>&limit=50" -Headers $h).Items.Count   # still returns items
```

## Phase 3 — Functional checks (desktop web client, Ctrl+F5 first)

1. **Continue Watching:** note an item from
   `(Invoke-RestMethod "$base/UserItems/Resume?userId=<id>&mediaTypes=Video" -Headers $h).Items | select Name, Id`.
   On the home screen open its "..." → **Remove from Continue Watching**. Card disappears.
   - The Resume call above no longer lists it.
   - `(Invoke-RestMethod "$base/Items/<itemId>?userId=<id>" -Headers $h).UserData` shows
     `PlaybackPositionTicks = 0`; `Played` and `PlayCount` unchanged.
   - Multi-version movie: the response `ResetItemIds` count equals the number of versions.
   - Wait ≥ 10 s, reload within 60 s: still gone. DevTools → WS frames show `UserDataChanged`.
2. **Next Up:** "..." on a Next Up card → **Remove from Next Up**. Card disappears.
   - `/Shows/NextUp?userId=<id>` omits the series; `limit=1` still returns one item.
   - Play any episode of that series → log line `Unhidden … Reason=playback`; the series returns.
3. **Auth:** API-key call to a user endpoint without `userId` → 400; `GET /HomeScreenCleanup/Admin/Hidden`
   with the API key → 200.
4. **Negative:** Latest/library cards and the multi-select menu show no entry; mobile and TV layouts
   show nothing and log no errors; a second user's removals do not affect the first.
5. **Config page:** Dashboard → Plugins → Home Screen Cleanup lists hidden series per user; Unhide works.
6. **Logs:** `Invoke-RestMethod "$base/System/Logs" -Headers $h` lists the files;
   `Invoke-RestMethod "$base/System/Logs/Log?name=<latest>" -Headers $h | Select-String HomeScreenCleanup`
   shows `StoreLoaded` with path `…\plugins\Jellyfin.Plugin.HomeScreenCleanup\hidden-series.json`
   and no exceptions.

## Phase 4 — Rollback

**Preferred:** Dashboard → Plugins → *Home Screen Cleanup* → **Uninstall** → restart.

**If the Dashboard is unusable** (on the server, elevated):

```powershell
Stop-Service JellyfinServer
Remove-Item -LiteralPath 'C:\ProgramData\Jellyfin\Server\plugins\Home Screen Cleanup_1.0.2.0' -Recurse
Start-Service JellyfinServer
```

The hidden list folder (`plugins\Jellyfin.Plugin.HomeScreenCleanup\`) can stay; delete it only to
discard all hidden series. Playback positions that were reset are not restored.

**Verify:** `/Plugins` no longer lists the plugin; `/web/index.html` has no `data-hsc-injected`.

## Manual install (fallback when the catalog is unavailable)

1. From the GitHub release `v1.0.2` download `home-screen-cleanup_1.0.2.0.zip` and
   `home-screen-cleanup_1.0.2.0.zip.sha256`.
2. Verify the hash — stop if it differs:

   ```powershell
   $zip  = '.\home-screen-cleanup_1.0.2.0.zip'
   $want = (Get-Content "$zip.sha256").Split(' ')[0]
   (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower() -eq $want   # must be True
   ```

3. Install:

   ```powershell
   Unblock-File $zip
   Stop-Service JellyfinServer
   Expand-Archive $zip -DestinationPath 'C:\ProgramData\Jellyfin\Server\plugins\Home Screen Cleanup_1.0.2.0'
   Start-Service JellyfinServer
   ```

   The folder inherits the service account's Modify right from `C:\ProgramData\Jellyfin\Server`.
4. Continue with Phase 1 **Verify**, then Phases 2–3.

## Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| Plugin not in Catalog | Raw GitHub cache, or repo URL typo | Wait 5 min; open the manifest URL in a browser. |
| Status `NotSupported` | Server is not 12.2.x | Check server version; roll back. |
| Status `Malfunctioned` | Startup/DI error | Read the log for `HomeScreenCleanup` exceptions; roll back. |
| No menu entries | Old cached `index.html`, script injection off, or mobile/TV layout | Ctrl+F5; check the config page toggle; use the desktop layout. |
| Removed card returns after reload | Reload within the 60 s web cache on another browser/profile | Wait 60 s; re-check `/UserItems/Resume`. |
