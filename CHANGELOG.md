# Changelog

All notable changes to this plugin. Versions follow the Jellyfin plugin scheme `X.Y.Z.0`
(git tag `vX.Y.Z`). Before tagging, replace `unreleased` with the release date (`YYYY-MM-DD`);
the release workflow refuses a section still marked unreleased.

## 1.0.2.0 — 2026-10-13

- Fix: the context-menu entry was never inserted because the entry button was created with a `createElement` options object, which jellyfin-web's custom-elements polyfill rejects; the failure also disabled the menu hook for the rest of the page load. The entry is now a plain button and an injection failure only skips that one menu.
- Enable the TV layout (Jellyfin Media Player TV mode, webOS/Tizen apps): the entry appears in the context menu opened by long-pressing OK or right-clicking a card.

## 1.0.1.0 — 2026-10-13

_1.0.0.0 was tagged but never published; the repository was recreated with clean history and the first public release is 1.0.1.0._

- **Remove from Continue Watching**: new entry in the "..." menu of Continue Watching cards. Resets
  the saved playback position of the item and every alternate version; played state, play count
  and last-played date are untouched.
- **Remove from Next Up**: new entry in the "..." menu of Next Up cards. Hides the series from Next
  Up for that user on every client; the series comes back automatically when the user plays any of
  its episodes. Admins can review and clear hidden series on the plugin configuration page.
