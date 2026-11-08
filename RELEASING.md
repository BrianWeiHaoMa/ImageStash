# Releasing ImageStash

How a release of ImageStash is made, for a person and for an agent following it step by step. A release is a GitHub
release made by the Release workflow from a tag; nothing else publishes one, and nothing in ImageStash looks for it.

## What a release holds

The release page `https://github.com/BrianWeiHaoMa/ImageStash/releases/tag/v<version>` holds three assets, all
written by `tools/build-release-assets.ps1` from one publish of the current tree:

- `ImageStash-<version>-win-x64.msi`: installs ImageStash for the signed-in user without administrator rights, under
  `%LOCALAPPDATA%\Programs\ImageStash`, with a Start Menu entry; a newer MSI upgrades it in place, and uninstalling
  removes the folder and leaves the stash and settings under `%LOCALAPPDATA%\ImageStash`.
- `ImageStash-<version>-win-x64.zip`: the same folder, under `ImageStash/`, to copy onto a PC and run.
- `SHA256SUMS.txt`: the SHA-256 of the MSI and the zip, in the `sha256sum` shape.

The release's title is `ImageStash <version>` and its notes are that version's section of `CHANGELOG.md`.
`https://github.com/BrianWeiHaoMa/ImageStash/releases/latest` resolves to the newest release once one exists.

## The version

The version has one home: `<Version>` in `Directory.Build.props`, three numbers `<major>.<minor>.<patch>`. Every
assembly, the MSI and the asset names carry it. Raise the patch for fixes, the minor for new features, and the major
for a change a person must know about before upgrading; no version may break what a newer build promises to open:
every stash, settings file and export an earlier build wrote (`PRODUCT_SPECIFICATION.md` G31, F11).

A release is the tag `v<version>` on the commit whose `Directory.Build.props` says `<version>`. The Release workflow
refuses a tag of another form, a tag naming another version, and a version with no section in `CHANGELOG.md`.

## Steps

1. On `main`, set `<Version>` in `Directory.Build.props` to the new version.
2. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [<version>] - <YYYY-MM-DD>` (today's date), keeping its
   `### Added`, `### Changed`, `### Fixed` or `### Removed` lines, and add an empty `## [Unreleased]` above it. Those
   lines become the release's notes; check them now with
   `powershell.exe -NoProfile -File tools/check-release.ps1 -Tag v<version> -Only identity -Notes $env:TEMP\notes.md`
   and read `$env:TEMP\notes.md`.
3. Commit both files, for example `Release <version>`.
4. Take the README's pictures again, so the tag's tree shows the build it releases: in the repository's Actions tab,
   run the workflow README screenshots (`.github/workflows/readme-screenshots.yml`) on `main`. It takes the pictures
   of `main` as pushed, so first check that `git log origin/main..main` lists only step 3's commit (the version and
   the changelog, which change no picture), pushing any commit beneath it with `git push origin HEAD~1:main`. Once
   it passes it has committed the pictures that changed to `main`, or nothing when none did; then
   `git pull --rebase origin main` puts its commit beneath yours. Or take them locally:
   `powershell.exe -NoProfile -File tools/make-readme-screenshots.ps1` from PowerShell on the interactive desktop,
   with ImageStash not running and `%LOCALAPPDATA%\ImageStash` moved aside, leaving the desktop to it for a minute;
   then commit what it changed under `docs/screenshots/`. A failed run changes no picture, and its last line names
   the scene that failed. A commit the workflow pushes starts no CI run of its own, which is why step 5 pushes yours
   after it.
5. Push `main` and wait for the CI workflow's run on that commit to pass (the repository's Actions tab, workflow CI).
   Its artifact `ImageStash-<version>-win-x64` holds the three assets that commit builds.
6. Tag and push the tag:
   `git tag -a v<version> -m "ImageStash <version>"`, then `git push origin v<version>`.
7. The Release workflow (`.github/workflows/release.yml`) runs on the tag: it checks the tag against
   `Directory.Build.props` and `CHANGELOG.md`, runs the whole suite on the tag's tree, and only once every phase has
   passed creates the release with the three assets and the notes, through `gh release create`.
8. Open the release page and check the three assets are there and that the notes are the changelog section.

### The first release

The first release is 1.0.0: `Directory.Build.props` already says `1.0.0`, so step 1 changes nothing; step 2 turns
`## [Unreleased]` into `## [1.0.0] - <date>`, and step 6 tags `v1.0.0`. `releases/latest` resolves only once that
release exists.

## The suite

`tools/check-release.ps1` is the whole suite a release must pass, and the only thing the workflows run. Its phases,
in order, stopping at the first that fails: `identity` (only with `-Tag`), `build`, `portable`
(`tests/ImageStash.Tests`), `desktop` (`tests/ImageStash.Windows.Tests`), `published` (`tools/check-published.ps1`,
where any `skipped:` line is a failure but the earlier-build phase's, below), `assets`
(`tools/build-release-assets.ps1`, at the version and at the version with its patch raised by one) and `installer`
(`tools/check-installer.ps1`, which installs, upgrades, repairs and uninstalls the MSI for the signed-in user). Its
comment-based help (`Get-Help tools/check-release.ps1 -Full`) lists its parameters.

To run it locally, from PowerShell (never Git Bash) at the repository root, on the interactive desktop, with ImageStash
not running, `%LOCALAPPDATA%\ImageStash` absent (move it aside first) and every window on the primary display
minimised:

```powershell
powershell.exe -NoProfile -File tools/check-release.ps1 -Output $env:TEMP\ImageStash-release
```

It takes about forty minutes and holds the desktop, the clipboard, the Run value `ImageStash`, the Recycle Bin and
`%LOCALAPPDATA%\ImageStash` meanwhile; it refuses to start while ImageStash runs or that folder exists. `-Output`
must be a new or empty folder, and `<Output>-upgrade` is written beside it. `-Only <phase>` runs one phase alone.
To build the assets without the suite: `powershell.exe -NoProfile -File tools/build-release-assets.ps1 -Output <empty folder>`.

### The earlier-build phase

`tools/check-published.ps1 -EarlierCommit <commit>` has a phase that publishes an earlier commit, has it fill a stash
and save settings, and opens them with the current build. The suite never passes `-EarlierCommit`, so every run prints
`skipped: no -EarlierCommit ...`, and that one `skipped:` line is allowed. The phase needs an earlier build whose store
is at user version 1, and every build in this history writes version 3, so no commit and no release can serve it. A
released build can serve as a later check's earlier build only once `tools/check-published-earlier-build.ps1` accepts
the store version of the commit it is given.

## When a run fails

- A failed CI run: the log's last `check-release: phase <phase> failed ...` line names the phase and the reason;
  `runner-desktop:` lines at the start say whether the runner's desktop was usable. Fix on `main` and push again.
- A failed Release run publishes nothing: the release is created by the workflow's last step, which runs only when
  every step before it passed. Fix the cause on `main`, then move the tag to the fixed commit and push it again:
  `git tag -d v<version>`, `git push origin :refs/tags/v<version>`, then step 6 on the fixed commit (with steps 1 to 3
  again if the fix changes the version or the notes).
- If a release was published and is wrong, do not reuse its version: delete nothing that people may have downloaded,
  make the fix, and release the next patch version.

## The runner

Both workflows run on `runs-on: ${{ vars.DESKTOP_RUNNER || 'windows-2025' }}`: GitHub's hosted Windows Server 2025
image unless the repository variable `DESKTOP_RUNNER` (Settings, Secrets and variables, Actions, Variables) names
another runner label, which switches every desktop job without a commit; delete the variable to go back.
`tools/runner-desktop.ps1`, the first step of each job, prints the session and the display and, on a GitHub runner,
raises a display smaller than 1920 x 1080 to 1920 x 1080, or else to 1280 x 800, failing the job on one line when
the session is not interactive or the display stays below 1280 x 800. Run locally, it only reports.

GitHub's hosted Windows runners run as an administrator with User Account Control off, so a run there shows the MSI
installs per user (no `ALLUSERS`, everything under `%LOCALAPPDATA%`) but cannot show it needs no administrator rights.
That is shown only by a run from a standard, unelevated account: run the suite locally from such a shell before the
first release and after any change under `installer/`, or use a self-hosted runner as below.

A self-hosted runner needs:

- Windows 11 x64 (or the Server equivalent), with the runner application started from a signed-in desktop session
  (its `run.cmd`), not installed as a Windows service, which runs in session 0 with no desktop to click or capture;
- a display of at least 1280 x 800 (the desktop checks are known to pass at 1280 x 800 and 100 % scale), left
  unlocked and without a screen saver while jobs run;
- a standard (not administrator) Windows account used only for this, since the suite uses that account's real
  `%LOCALAPPDATA%\ImageStash`, Run value, clipboard and Recycle Bin, and installs and uninstalls the MSI for it;
- Git, Windows PowerShell 5.1, Microsoft Edge (some desktop checks open it), the .NET SDK that `global.json` names
  (`actions/setup-dotnet` installs it when missing) and, for releases, the GitHub CLI `gh` on the `PATH`;
- the label set in `DESKTOP_RUNNER`.
