# Changelog

Every release of ImageStash and what it changed, newest first, in the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) shape.

## [Unreleased]

### Changed

- Search now lists first the pictures where every word you typed starts a word of the name or of a tag, then the rest, and within each, the ones you used most recently first, then those never used, newest first. So typing `re` and Enter pastes the "really" reaction you post every day, not the newest screenshot, whose name holds those letters only inside a word. Everything search found before it still finds; Recent, Favorites and All list as before.

## [0.2.2] - 2026-11-16

### Fixed

- In the editor's Trim tool, Reset and the hint beside it now stay where they are while the clip plays and while you drag the handles, instead of shifting a little as the time and the kept range change.
- Revert to original, and saving an edit again, on an animated GIF or WebP that is playing in the grid or the preview now works, instead of failing after a few seconds with "another program is holding it open" when ImageStash itself was the one holding it.
- When a Revert to original does fail because another program holds the picture, the item now still shows as edited, with Revert to original offered again, until the picture really goes back; before, the menus dropped Revert while the edited picture stayed.

## [0.2.1] - 2026-11-14

### Fixed

- A pinned stash window that was behind another app when a screenshot or recording started now goes back behind that app when it ends, instead of coming up in front of it unfocused, where clicking the app did not bring the app back in front.

## [0.2.0] - 2026-11-11

### Added

- Two larger thumbnail sizes: `Ctrl`+wheel and `Ctrl` with `+` or `-` now step the grid through five, and Settings has a Thumbnail size list that the grid follows at once.

### Changed

- A screenshot's or recording's notification now goes away when you click its Edit, its Trim or the notification itself, so it no longer lies over the editor or the window it opened.
- The cross that starts a screenshot's or recording's drag is now light with a dark edge, so it shows on bright and dark backgrounds alike.

### Fixed

- A recording of busy content, such as a playing video, keeps about 15 frames a second instead of falling to one or two.
- A screenshot started right after you click into another app no longer brings the stash window back over that app: a pinned window comes back behind it instead of taking your typing, and a flyout stays hidden.

## [0.1.0] - 2026-11-10

### Added

- A stash of your reaction pictures and GIFs that lives in the Windows 11 tray and starts with Windows.
- The paste moment: `Ctrl+Alt+Space` opens the stash beside the text caret, two typed letters find a picture, and Enter pastes it into the chat you were typing in without ever sending the message.
- Filling the stash: drop files, a folder or pictures from a browser onto the window, paste a copied picture with `Ctrl+V`, import files with `Ctrl+O`, screenshot a region with `Ctrl+PrtScn`, record a few seconds of the screen as a GIF with `Ctrl+Shift+PrtScn`, and stash the clipboard's picture with `Ctrl+Alt+V`.
- PNG and JPEG as stills (an animated PNG is handled as a still), and GIF and WebP, still or animated, kept as the files they came as.
- Finding pictures again by thumbnail, name, tag, favorite and recent use, with tag chips and a search that narrows as you type.
- Renaming, tagging, favoriting, multi-selecting and deleting with Undo; dragging a picture out as a file; copying with `Ctrl+C`; Save a copy, Open with and Reveal in File Explorer.
- An editor with five tools (Text, Crop, Trim, Hide and Draw) whose edits stay as layers over the untouched original, so every edit can be changed or reverted later.
- Settings with the four shortcuts, Also copy a capture, the recording's length and pointer, the stash's location with Move, Start with Windows and Theme.
- Export stash and Import stash, to carry the whole stash and its settings to another PC or account.
- An MSI installer that installs ImageStash for the signed-in user without administrator rights, upgrades it in place and removes it again, and a zip of the same folder to copy onto a PC and run.
