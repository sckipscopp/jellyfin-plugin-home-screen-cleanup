# Changelog

All notable changes to this plugin. Versions follow the Jellyfin plugin scheme `X.Y.Z.0`
(git tag `vX.Y.Z`). Before tagging, replace `unreleased` with the release date (`YYYY-MM-DD`);
the release workflow refuses a section still marked unreleased.

## 1.0.0.0 — 2026-10-13

- **Remove from Continue Watching**: new entry in the "..." menu of Continue Watching cards. Resets
  the saved playback position of the item and every alternate version; played state, play count
  and last-played date are untouched.
- **Remove from Next Up**: new entry in the "..." menu of Next Up cards. Hides the series from Next
  Up for that user on every client; the series comes back automatically when the user plays any of
  its episodes. Admins can review and clear hidden series on the plugin configuration page.
