# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub:
**Security → Report a vulnerability** on
<https://github.com/sckipscopp/jellyfin-plugin-home-screen-cleanup/security/advisories/new>.

Do not open a public issue for a security problem. Include the plugin version, the Jellyfin server
version and steps to reproduce. Expect an acknowledgement within a week.

## Supported versions

| Plugin version | Jellyfin server | Supported |
|---|---|---|
| 1.0.x.0 | 12.2.x | Yes |
| anything older | — | No |

Only the latest release receives fixes.

## Design notes relevant to security

- The plugin holds **no secrets**: no API keys, tokens or credentials are stored, logged or
  shipped. Its only persistent state is `hidden-series.json` (user id, series id, series name,
  timestamp) in the plugin data folder.
- Every endpoint requires an authenticated Jellyfin session except `GET /HomeScreenCleanup/client.js`
  (a static script). Users act only on their own data; acting on another user requires an
  administrator. Admin endpoints require the `RequiresElevation` policy.
- Items the caller cannot see return 404, so endpoints are not an existence oracle.
- Logs contain GUIDs only — never user names, item names or tokens.
- The web page is changed only in memory (one `<script>` tag); nothing on disk is modified.
- Releases are built by GitHub Actions from a tag on `main` with SHA-pinned actions, locked NuGet
  restore and least-privilege tokens; each release ships `.md5` and `.sha256` checksums.
