# Changelog

Every release of ImageStash and what it changed, newest first, in the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) shape.

## [Unreleased]

### Added

- A copy now says it worked: after `Ctrl+C`, an Enter that copies (with no app to paste into), or the item menu's Copy, the footer reads "✓ Copied" with the picture's name, or how many pictures, for two seconds before its hints return. In the preview, the button that copies reads "Copied ✓" for a moment instead. A paste into a chat shows nothing of the kind: ImageStash cannot see whether it arrived.

### Changed

- With several pictures selected, the footer now says how many Enter hands over ("Enter · Paste 3 items into Slack", or "Enter · Copy 3 items"), and so do the item menu's paste and Copy entries, so a selection left over from tidying is never pasted unannounced.

- Search now lists first the pictures where every word you typed starts a word of the name or of a tag, then the rest, and within each, the ones you used most recently first, then those never used, newest first. So typing `re` and Enter pastes the "really" reaction you post every day, not the newest screenshot, whose name holds those letters only inside a word. Everything search found before it still finds; Recent, Favorites and All list as before.
- After a paste hides the stash window, the next shortcut opens it on Recent with an empty search box, the picture you just pasted first and selected on its own, so "shortcut, Enter" pastes it again and two typed letters start a fresh search. Hiding it any other way (clicking back into the chat, the shortcut, Esc) still leaves your search and selection as they were. The pinned window is left as it is.
- A preview open in the stash window now closes when the window hides, so the next shortcut lands in the search box and typed letters search, instead of finding the old preview, where Enter would paste that picture again. The pinned window keeps its preview; so does a preview whose name or tag you were still typing, and one a screenshot or recording hid for a moment and put back.
- After importing several pictures, they are no longer all still selected the next time you open the window, where Enter would paste them all: the first of them alone is. Several pictures you selected yourself stay selected.
- `Ctrl+C` right after the shortcut opens the window now copies the selected picture, as the footer says, instead of the leftover search word the window had selected so that typing replaces it. Text you select in the search box yourself (by dragging, Shift with an arrow, Home or End, or `Ctrl+A`) is still copied as text.
- A notice in the stash window now stays as long as its words take to read: six seconds for a short one, and for a longer one two seconds plus one for every three and a half words, up to twenty. Its time runs only while the window is showing, so a result that arrives while the window is hidden or minimised, such as an import that ends after you switched back to your chat, is still there, with its whole time ahead of it, when you next open the window. Move…, Export stash… and Import stash… no longer wait for ever on a notice in a hidden window: its Undo ends as if its time were up, and they go ahead once what that ends has finished. Notifications above the tray still stay six seconds.

### Fixed

- Holding Enter down no longer pastes the picture twice or reaches the chat as Send: one press hands over once, however long it is held, and the chat is brought forward only once Enter is let go. Still held a second after the picture is ready, nothing is pasted: the picture is on the clipboard and the "Copied · press Ctrl+V" notification says so. An Enter that committed a name or a tag in the preview does not also paste.

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
