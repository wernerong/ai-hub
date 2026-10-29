# AI Hub

<img src="Assets/AIHub.png" alt="AI Hub icon" width="128" />

A Windows desktop workspace for the AI apps you use. Installed or open AI apps
appear in the sidebar automatically. Connect other desktop apps or dedicated
browser windows with **Connect an AI**, keeping their existing accounts and
conversations. A dark, cyan HUD shows available usage monitors with remaining
allowance rings, reset countdowns and amber warnings.

## Run

Download **AIHub-vX.Y.Z-windows-x64-Setup.exe** from [the latest public release](https://github.com/wernerong/ai-hub/releases/latest)
and double-click it. Close AI Hub first if upgrading. Setup installs for your
Windows account, creates shortcuts and registers a native uninstaller in
Installed apps. No administrator access, PowerShell commands, execution-policy
changes or ZIP extraction are needed. Existing settings remain in place.
The installer is currently unsigned, so Windows may show an unknown-publisher
or SmartScreen warning. Checksums are provided in `SHA256SUMS.txt`.

The ZIP remains available for portable use and automatic updates. To run it
portably, extract it to a writable folder and open `AIHub.exe`. Its legacy
PowerShell installation scripts are retained for compatibility; **Setup.exe**
is the recommended installation method.
The .NET runtime is included. Open or install your AI apps separately.

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

Requires Windows and the .NET 10 SDK. Packaging also requires Inno Setup 6.7+
(`ISCC.exe` on PATH or selected with the `INNO_SETUP_COMPILER` environment variable).

```powershell
.\Build.ps1
.\Package.ps1
```

Build writes to `dist`. Package builds, runs the Windows integration self-test,
and produces a native Setup.exe, a versioned ZIP and SHA-256 checksums in `artifacts`.
Pass `-OutputDirectory` to choose another destination. Package staging folders
must be new so stale files cannot enter a release.

## Behavior and limits

### App discovery and connections

Version 0.4.0 discovers OpenAI, Grok, Grok Bot, Claude, Gemini, Perplexity,
DeepSeek, LM Studio and Msty from Windows app registrations, Start menu
shortcuts or matching open windows. Apps you do not have stay out of the
sidebar. Discovery refreshes open windows every five seconds and installed
apps every minute. Hosted windows stay available while hidden by the hub.

For another AI, select **+** in the sidebar, open its app, and choose its window.
Use a separate browser window for a website: AI Hub hosts the whole window.
Custom desktop connections remember the executable path; custom browser
connections remember that path and the selected title. Reconnect a browser
window if its title changes after it closes.

Right-click any AI's sidebar button and choose **Remove from AI Hub**. This
restores its original window and remembers your choice across restarts, even
if the app is still installed or running. You can also remove apps from the
**+ / Connect an AI** dialog. Choose a removed app there and **Add back to AI
Hub** to restore it. Removing an app does not uninstall it or erase its account.
Usage monitoring can be enabled or disabled separately in Settings.
Connections are local to this Windows account in
`%LOCALAPPDATA%\AIHub\connections.json`; removed choices are stored alongside
them in `connections.hidden.json`. Claude uses its official desktop app icon.

App hosting and usage monitoring are separate capabilities. Usage cards appear
only for detected, enabled readers: Codex CLI, Grok CLI / Build login and Grok
Bot. Other AI apps can be connected without adding an unsupported usage card.
The dashboard adapts to zero, one or several monitors. Settings preserves
monitoring choices even while a provider is absent. Content zoom controls are
available for the original OpenAI, Grok and Grok Bot integrations; other apps
keep their own zoom controls.

### Window behavior and usage

Version 0.4.1 makes clicking AI Hub's taskbar button minimize the workspace
while a hosted AI has focus. The next click restores the selected AI. The
handler pairs a brief taskbar mouse click with activation of this hub, ignores
drags and modified clicks, and does not intercept input or record mouse history.
Title-bar minimization immediately hides hosted windows. Their independent
window identities, accounts and crash recovery remain intact.

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
