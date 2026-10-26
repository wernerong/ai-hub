# AI Hub

<img src="Assets/AIHub.png" alt="AI Hub icon" width="128" />

A Windows desktop workspace for your installed OpenAI, Grok and Grok Bot apps.
Switch between the three panels, keep existing accounts and conversations, and
adjust each app's content zoom from Settings. OpenAI's own controls switch
between ChatGPT, Codex and Dot.

## Run

Download the Windows x64 ZIP from Releases, extract it and run `AIHub.exe`.
The .NET runtime is included. The AI apps must already be installed.

The version stays visible in the title bar and sidebar. **Pop out** restores
one app's original window; **Restore all** or closing the hub restores them all.

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
