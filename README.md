# AI Hub

<img src="Assets/AIHub.png" alt="AI Hub icon" width="128" />

A Windows desktop workspace for your installed OpenAI, Grok and Grok Bot apps.
Switch between the three panels, keep existing accounts and conversations, and
adjust each app's content zoom from Settings. The compact bottom strip monitors
Codex, Grok CLI / Build and Grok Bot allowances. OpenAI's own controls switch
between ChatGPT, Codex and Dot.

## Run

Download the Windows x64 ZIP from [the latest public release](https://github.com/wernerong/ai-hub/releases/latest),
verify it against `SHA256SUMS.txt`, and extract it. Run `Install-AIHub.ps1` in
PowerShell to install for your Windows account with shortcuts and an entry in
Installed apps. You can also run `AIHub.exe` from a writable folder.
The .NET runtime is included. The AI apps must already be installed.

The version stays visible in the title bar and sidebar. **Pop out** restores
one app's original window; **Restore all** or closing the hub restores them all.

## Automatic updates

Version 0.3.0 is the first public release. It checks published stable GitHub
releases at startup and every six hours, verifies the download's SHA-256 digest,
and installs when AI Hub closes after restoring its app windows. In **Settings**,
choose **Check for updates**, **Restart to update**, or turn automatic updates off.
Updates do not need a GitHub account. Builds before 0.3.0 need one manual upgrade.
Your provider accounts, zoom choices and usage preferences stay in place.

Publishing a GitHub release delivers an update; pushing source alone does not.
The [local release script](docs/RELEASING.md) builds and uploads verified assets
without using GitHub Actions jobs, minutes, artifact storage or cache storage.

## Build and package

Requires Windows and the .NET 10 SDK.

```powershell
.\Build.ps1
.\Package.ps1
```

Build writes to `dist`. Package builds, runs the Windows integration self-test,
and produces a versioned ZIP and SHA-256 checksum in `artifacts`.
Pass `-OutputDirectory` to choose another destination. Package staging folders
must be new so stale files cannot enter a release.

## Behavior and limits

Version 0.3.1 keeps the selected AI app visible when you return from another
window, without requiring another sidebar click. It waits for mouse presses
to finish before restoring app focus, and leaves other applications in front
while AI Hub is in the background.

Version 0.2.1 fixes OpenAI extending over the bottom usage strip when its
standalone minimum window size exceeds the workspace. Constrained windows
resize to fit the panel; popping them out restores their normal sizing rules.

Version 0.2.0 adds an integrated Usage page. Click any bottom indicator or
**Usage** in the rail for all reported windows, reset countdowns, credits and
connection errors. Each indicator shows the most constrained reported window.
Readings refresh independently every minute; errors retain the last successful
reading and mark it stale. Missing values remain unavailable.

**Settings** controls automatic updates, provider monitoring, optional low-allowance and confirmed
reset notifications, the low-allowance threshold, Windows startup, and app zoom.
Notifications default off and deduplicate across restarts. Reset notifications
require a fresh reading with a newer reset window and reduced usage, not just
the passage of a countdown deadline.

Codex usage applies to Codex, not ChatGPT or Dot. Grok usage is CLI / Build
allowance, not Grok web chat. Existing provider logins remain with their apps;
AI Hub reads them in memory to request usage and does not save tokens. Usage
settings and alert identifiers live in `%LOCALAPPDATA%\AIHub\usage-settings.json`.

The monitoring code is a separate UI-independent .NET project in `Usage/`,
adapted from AI Usage Widget. The standalone widget remains independently
installed; close it when using AI Hub for monitoring to avoid duplicate reads.
This update does not alter its settings or startup behavior.

The hub aligns independent native windows over its content area and hides
inactive apps. A separate helper restores original styles and positions after
an unexpected exit. It does not merge accounts or use model API keys.

Zoom changes AI app content only. Settings are stored locally in
`%LOCALAPPDATA%\AIHub\zoom-settings.json`. OpenAI's active browser pane can
receive zoom shortcuts; focus the chat view before applying zoom to that view.
Apps may open their own dialogs separately. This preview still needs broader
testing across multiple monitors, virtual desktops and voice/video workflows.

See [README.txt](README.txt) for usage, recovery details and validation scope.
The icon was created with built-in image generation; its prompt is recorded in
[Assets/icon-prompt.txt](Assets/icon-prompt.txt).
Provider artwork is attributed in [Assets/Providers/README.txt](Assets/Providers/README.txt).
See [CHANGELOG.md](CHANGELOG.md) for release history.

To exercise foreground activation with disposable windows, run
`AIHub.exe --activation-test <absolute-output-folder>` and click **Run activation
checks**. These interactive checks complement the background self-test because
Windows requires foreground permission to reproduce switching away and back.
