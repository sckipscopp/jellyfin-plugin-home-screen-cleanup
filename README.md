# Home Screen Cleanup (Jellyfin plugin)

Adds two entries to the **"..."** menu of cards on the Jellyfin web home screen:

| Row | Menu entry | What it does |
|---|---|---|
| Continue Watching | **Remove from Continue Watching** | Resets the saved playback position to 0 on the item **and every alternate version** (e.g. 1080p + 4K). `Played`, play count and last-played date are **not** changed, so the item stays watched/unwatched exactly as it was. |
| Next Up | **Remove from Next Up** | Hides the series from Next Up **for your user only**. The filter runs on the server, so it applies to **every client** (web, mobile, TV apps). Playing any episode of the series un-hides it automatically. |

Side effect to know about: a TV episode removed from Continue Watching has no resume position
anymore, so it can now show up in **Next Up** (the web client asks Next Up to skip resumable
episodes; after the reset it no longer is one). Use "Remove from Next Up" if you want it gone
from both rows.

Tested with Jellyfin server **12.2.0** / jellyfin-web **12.2**.

## Install

1. Dashboard → **Plugins** → **Repositories** → **+** and add:

   ```
   https://raw.githubusercontent.com/sckipscopp/jellyfin-plugin-home-screen-cleanup/main/manifest.json
   ```

2. **Catalog** → *Home Screen Cleanup* → **Install**, then restart the server.
3. Dashboard → Plugins should list *Home Screen Cleanup* as **Active**.
4. Reload the web client (Ctrl+F5 once).

Raw GitHub content is cached for about 5 minutes, so a brand-new release can take that long to
appear in the catalog. Consider turning off auto-update for this plugin so updates are deliberate.
A step-by-step runbook with verification, manual install and rollback is in
[docs/install.md](docs/install.md).

## How it works

- **Script injection, in memory only.** The plugin adds one middleware that rewrites the response
  for `/web/`, `/web/index.html` (base URL aware) and inserts a single
  `<script defer src="../HomeScreenCleanup/client.js?v=…">` tag before `</body>`. Nothing in the
  `jellyfin-web` folder on disk is touched, so it works when the server account cannot write there
  and survives server upgrades. Turn it off with **Load the menu script in the web client** on the
  config page.
- **client.js** watches for the "..." action sheet opened from a Continue Watching or Next Up card
  and adds the matching entry. If jellyfin-web changes its markup, the script disables itself
  quietly (one `console.debug` line) instead of breaking the page.
- **Continue Watching** calls `POST /HomeScreenCleanup/ContinueWatching/{itemId}/Remove`.
- **Next Up** calls `POST /HomeScreenCleanup/NextUp/Hidden/{itemId}`. The hidden list lives in
  `plugins/Jellyfin.Plugin.HomeScreenCleanup/hidden-series.json` under the server data folder, and
  an MVC filter removes hidden series from `/Shows/NextUp` responses (paging preserved).
- The web client caches home rows for up to 60 seconds; the script filters removed cards out of
  those cached replays so they do not flicker back.

## API

All routes are under the server base URL. User routes act on the caller's own user; an
administrator may add `?userId=` to act on someone else (API keys must always pass `userId`).

| Verb + route | Auth | Result |
|---|---|---|
| `POST /HomeScreenCleanup/ContinueWatching/{itemId}/Remove` | user | 200 `{UserId, ItemId, ResetItemIds[], Changed}`; 404 not found/not visible; 400 not a video |
| `POST /HomeScreenCleanup/NextUp/Hidden/{itemId}` (episode or series id) | user | 200 `{SeriesId, SeriesName, HiddenAtUtc, AlreadyHidden}`; 404/400 as above; 409 at the per-user cap |
| `DELETE /HomeScreenCleanup/NextUp/Hidden/{seriesId}` | user | 204 (idempotent) |
| `GET /HomeScreenCleanup/NextUp/Hidden` | user | 200 list of the caller's hidden series |
| `GET /HomeScreenCleanup/ClientConfig` | user | 200 `{EnableContinueWatchingAction, EnableNextUpAction}` |
| `GET /HomeScreenCleanup/client.js` | anonymous | the web client script |
| `GET /HomeScreenCleanup/Admin/Hidden` | admin | every user's hidden list |
| `DELETE /HomeScreenCleanup/Admin/Hidden/{userId}/{seriesId}` | admin | unhide one series for a user |
| `DELETE /HomeScreenCleanup/Admin/Hidden/{userId}` | admin | clear a user's list |

## Configuration page

Dashboard → Plugins → *Home Screen Cleanup*:

- **Load the menu script in the web client** — adds the plugin's script to the web client page.
  Turn off to remove both menu entries from the web client; the Next Up filter keeps working.
  Takes effect when the web client is reloaded.
- **Unhide a series when the user plays one of its episodes** — auto-unhide on playback.
- **Show "Remove from Continue Watching"** — offers the entry on Continue Watching cards.
- **Show "Remove from Next Up"** — offers the entry on Next Up cards. Series already hidden stay
  hidden when this is turned off; unhide them in the list below.
- **Hidden from Next Up** — series each user has hidden, with **Unhide** per series and
  **Clear all** per user (deleted users show as `(deleted user) <id>`).

Click **Save**; no server restart is needed.

## Security

- Least privilege: every API endpoint requires a signed-in user and acts on that user's own data.
  Acting on another user (`?userId=`) and all `Admin/*` endpoints require an administrator.
  API keys must name a user explicitly. Items you cannot see return 404.
- The only anonymous endpoint is `GET /HomeScreenCleanup/client.js` (a static file).
- No secrets are stored; logs contain GUIDs only, never user names, item names or tokens.
- The client never uses `innerHTML` or `eval`; all text is set with `textContent`.
- Supply chain: SHA-pinned GitHub Actions with least-privilege tokens, central package versions
  with committed lock files and locked-mode restore, nuget.org-only source mapping, NuGet audit,
  Dependabot with cooldown, releases only from tags on `main`, `.md5` + `.sha256` per release.

Report vulnerabilities privately — see [SECURITY.md](SECURITY.md).

## Compatibility

- Jellyfin server **12.2.x** (`targetAbi 12.2.0.0`, .NET 10). Jellyfin 13 will need a new build.
- Menu entries appear in the **desktop web layout** (browser, Jellyfin Media Player). Mobile and TV
  layouts have no "..." button on home cards, so no entry is shown there; the Next Up hide still
  applies to every client.
- Only the video Continue Watching row is handled (not *Continue Listening* / *Continue Reading*).
- If a client asks for `enableTotalRecordCount=true` on `/Shows/NextUp`, the returned total can exceed the real count by at most the number of hidden series (hidden items outside the fetched window are not subtracted). The web client never asks for a total.
- Downgrading the plugin to a build with an older store schema moves `hidden-series.json` aside as `hidden-series.corrupt-<stamp>.json` and starts empty; the file is kept, not deleted.

## Uninstall

Dashboard → Plugins → *Home Screen Cleanup* → **Uninstall**, then restart the server. Reset
playback positions stay reset. The hidden list stays in
`plugins/Jellyfin.Plugin.HomeScreenCleanup/` but is no longer applied; delete that folder to remove
it completely.

## Building

```
dotnet restore Jellyfin.Plugin.HomeScreenCleanup.slnx --locked-mode
dotnet build Jellyfin.Plugin.HomeScreenCleanup.slnx -c Release --no-restore -warnaserror
dotnet test Jellyfin.Plugin.HomeScreenCleanup.slnx -c Release --no-build
```

Requires the .NET SDK pinned in `global.json`. Tests use xunit.v3 on **Microsoft.Testing.Platform**
(`global.json` sets `"test": {"runner": "Microsoft.Testing.Platform"}`; the .NET 10 SDK no longer
runs xunit.v3 through VSTest). `dotnet test` works as above; the test project is also a runnable
executable: `dotnet run --project tests/Jellyfin.Plugin.HomeScreenCleanup.Tests -c Release`. Releases are produced by `.github/workflows/release.yml`
when a `vX.Y.Z` tag on `main` is pushed.

## License

[GPL-3.0-only](LICENSE), the same license as the Jellyfin server packages it builds against.
