# Releasing ImageStash

How a release of ImageStash is made, for a person and for an agent following it step by step. A release is a GitHub
release made by the Release workflow from a tag; nothing else publishes one, and nothing in ImageStash looks for it.

## What a release holds

The release page `https://github.com/BrianWeiHaoMa/ImageStash/releases/tag/v<version>` holds three assets, all
written by `tools/build-release-assets.ps1` from one publish of the current tree:

- `ImageStash-<version>-win-x64.msi`: installs ImageStash for the signed-in user without administrator rights, under
  `%LOCALAPPDATA%\Programs\ImageStash`, with a Start Menu entry; a newer MSI upgrades it in place, and uninstalling
  removes what it installed, and the folder once nothing else is in it, and leaves the stash and settings under
  `%LOCALAPPDATA%\ImageStash`, and anything the person keeps in the install folder.
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
4. Take the README's pictures again, so the tag's tree shows the build it releases:
   `powershell.exe -NoProfile -File tools/make-readme-screenshots.ps1` from PowerShell on the interactive desktop of
   a Windows PC, with ImageStash not running, `%LOCALAPPDATA%\ImageStash` moved aside and every window on the primary
   display minimised, leaving the desktop to it for a minute; then commit what it changed under `docs/screenshots/`.
   A failed run changes no picture, and its last line names the scene that failed. This local run is the route seen
   working. The workflow README screenshots (`.github/workflows/readme-screenshots.yml`, dispatched by hand from the
   Actions tab) runs the same script and commits the pictures that changed to the branch, but it has never been run
   on GitHub's hosted image, whose elevated job may refuse its scenes (*The runner*).
5. Run the whole suite on this tree, on a Windows PC, as *The suite* says:
   `powershell.exe -NoProfile -File tools/check-release.ps1 -Output $env:TEMP\ImageStash-release`, about forty
   minutes. Go on only once it ends `check-release: every phase held: build, portable, desktop, published, assets,
   installer (<n> min)`. This run is the release's gate for every phase the workflows cannot run; a fix it
   needs is committed, and the run repeated, before step 6.
6. Push `main` and wait for the CI workflow's run on that commit to pass (the repository's Actions tab, workflow CI).
   It runs the suite's hosted phases (*The runner*); its artifact `ImageStash-<version>-win-x64` holds the three
   assets that commit builds.
7. Tag and push the tag:
   `git tag -a v<version> -m "ImageStash <version>"`, then `git push origin v<version>`.
8. The Release workflow (`.github/workflows/release.yml`) runs on the tag: it checks the tag against
   `Directory.Build.props` and `CHANGELOG.md`, runs the suite's hosted phases on the tag's tree, and only once they
   have passed creates the release with the three assets and the notes, through `gh release create`.
9. Open the release page and check the three assets are there and that the notes are the changelog section.

### The first release

The first release is 1.0.0: `Directory.Build.props` already says `1.0.0`, so step 1 changes nothing; step 2 turns
`## [Unreleased]` into `## [1.0.0] - <date>`, and step 7 tags `v1.0.0`. `releases/latest` resolves only once that
release exists.

## The suite

`tools/check-release.ps1` is the whole suite a release must pass. Its phases, in order, stopping at the first that
fails: `identity` (only with `-Tag`), `build`, `portable` (`tests/ImageStash.Tests`), `desktop`
(`tests/ImageStash.Windows.Tests`; in both, a check skipped as inconclusive is a failure), `published`
(`tools/check-published.ps1`, where any `skipped:` line is a failure but the earlier-build phase's, below), `assets`
(`tools/build-release-assets.ps1`, at the version and at the version with its patch raised by one) and `installer`
(`tools/check-installer.ps1`, which installs, upgrades, repairs and uninstalls the MSI for the signed-in user). With
`-NoDesktop` it runs only `identity` (with `-Tag`), `build`, `portable` and `assets`, the phases that need no
desktop, which is what the workflows run. Its comment-based help (`Get-Help tools/check-release.ps1 -Full`) lists its
parameters.

The whole run is a person's, on a Windows 11 x64 PC: from an unelevated PowerShell (never Git Bash, and not "Run as
administrator"; a standard account, or an administrator account with User Account Control on, as Windows sets it)
at the repository root, on the interactive desktop, since some desktop checks confirm they run unelevated as
ImageStash does and the installer phase installs from that shell to show the MSI needs no administrator rights;
with a primary display whose working area holds 1280 x 752 device-independent pixels at its scale (below), left
unlocked and awake for the whole run (a locked or sleeping desktop takes no input, and the desktop checks then time
out); the .NET SDK
`global.json` pins installed (`dotnet --version` in the repository root prints it), and the internet reachable for
the first package restore; Microsoft Edge installed (some desktop checks open it); ImageStash not running,
`%LOCALAPPDATA%\ImageStash` absent (move it aside first) and every window on the primary display minimised:

```powershell
powershell.exe -NoProfile -File tools/check-release.ps1 -Output $env:TEMP\ImageStash-release
```

The display condition is the 1280 x 800 display at 100 % the desktop checks are written for, less Windows 11's
taskbar, 48 device-independent pixels high; the checks place their windows from the primary display's size and scale,
so a larger working area serves too. In device-independent pixels the working area is the display's size divided by
its scale, less the taskbar: 1920 x 1080 holds the condition at 100 % and 125 % (1536 x 816), not at 150 %
(1280 x 672: 672 is short of 752); 2560 x 1440 at 100 % to 175 % (1462 x 774 at 175 %), not at 200 % (1280 x 672) or
225 % (1137 x 592); 3840 x 2160 at 200 % (1920 x 1032) and 225 % (1706 x 912). Below it the run fails for the
display's size, not for ImageStash, so first choose a scale the display holds it at:
`powershell.exe -NoProfile -File tools/display-scale.ps1` lists each display with its current and offered scales, and
`-Set <percent>` sets the primary display's, unelevated and at once, as Settings > Display > Scale does (set it back
afterwards). Seen passing on a 2560 x 1440 primary beside a 1920 x 1080 display at 100 %: the desktop checks whole at
each of 100 %, 125 %, 150 % and 175 % (675 checks, about fourteen minutes at each scale); and on a 1920 x 1080
primary beside the 2560 x 1440 display at 150 %, at each of 100 % and 125 %. One whole run at 175 % failed a single
timing check, `CaptureCardTests.WhileTheFieldHasFocusTheNoticeOutlivesItsSixSecondsAndItsTimeRunsOnOnceFocusHasGone`
(the card still up after its time had run on), which passed alone five times there and in the next whole run. The
published check at 150 % on the
2560 x 1440 primary, and at 125 % on the 1920 x 1080 one: every phase held in some runs; in the others every phase held
but the Move phase, whose folder picker sometimes reads an empty address bar after the typed path, at 100 % as well,
and once the paste-in phase, which found File Explorer focused after the open shortcut, also seen at 100 %.

It takes about forty minutes and holds the desktop, the clipboard, the Run value `ImageStash`, the Recycle Bin and
`%LOCALAPPDATA%\ImageStash` meanwhile; it refuses to start while ImageStash runs or that folder exists. `-Output`
must be a new or empty folder, and `<Output>-upgrade` is written beside it. `-Only <phase>` runs one phase alone.
The hosted phases alone, as the workflows run them, need no desktop and take minutes (six on a PC, about a quarter
of an hour on the hosted runner):
`powershell.exe -NoProfile -File tools/check-release.ps1 -NoDesktop -Output <empty folder>`.
To build the assets without the suite: `powershell.exe -NoProfile -File tools/build-release-assets.ps1 -Output <empty folder>`.

### The earlier-build phase

`tools/check-published.ps1 -EarlierCommit <commit>` has a phase that publishes an earlier commit, has it fill a stash
and save settings, and opens them with the current build. The suite never passes `-EarlierCommit`, so every run prints
`skipped: no -EarlierCommit ...`, and that one `skipped:` line is allowed. The phase needs an earlier build whose store
is at user version 1, and every build in this history writes version 3, so no commit and no release can serve it. A
released build can serve as a later check's earlier build only once `tools/check-published-earlier-build.ps1` accepts
the store version of the commit it is given.

## When a run fails

- A failed local run or CI run: the last `check-release: phase <phase> failed ...` line names the phase and the
  reason. Fix on `main` and go on from step 5 with the fixed commit, so that the commit you tag is one the whole
  suite passed on.
- A failed Release run publishes nothing: the release is created by the workflow's last step, which runs only when
  every step before it passed. Fix the cause on `main`, then move the tag to the fixed commit and push it again:
  `git tag -d v<version>`, `git push origin :refs/tags/v<version>`, then steps 5 to 7 on the fixed commit (with
  steps 1 to 3 again if the fix changes the version or the notes).
- If a release was published and is wrong, do not reuse its version: delete nothing that people may have downloaded,
  make the fix, and release the next patch version.

## The runner

The CI and Release workflows run on GitHub's hosted Windows Server 2025 image (`runs-on: windows-2025`) and run
`tools/check-release.ps1 -NoDesktop`: the tag's identity (Release only), the build, the portable checks and the
assets. The hosted job runs as an administrator with User Account Control off, so the other phases cannot judge
ImageStash there: some desktop checks confirm they run unelevated, as ImageStash does, and rightly fail on that job;
the published check drives the same desktop; and an elevated install cannot show that the MSI needs no
administrator rights. Those phases are step 5's local run from an unelevated shell.

The README screenshots workflow is the one job that needs a desktop. It runs on
`runs-on: ${{ vars.DESKTOP_RUNNER || 'windows-2025' }}`: the hosted image unless the repository variable
`DESKTOP_RUNNER` (Settings, Secrets and variables, Actions, Variables) names another runner label. Its first step,
`tools/runner-desktop.ps1`, prints the session and the display and, on a GitHub runner, raises a display smaller than
1280 x 800 to that size, failing the job on one line when the session is not interactive or the display stays
smaller; run locally, it only reports. Whether the pictures' scenes pass on the hosted image is untried; step 4's
local run is the route seen working.
