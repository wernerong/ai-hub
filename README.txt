AI Hub 0.4.0 — Windows desktop workspace

0.4.0: Your apps, discovered automatically; a new HUD usage dashboard
Only installed or open AI apps appear in the sidebar. Built-in discovery covers
OpenAI, Grok, Grok Bot, Claude, Gemini, Perplexity, DeepSeek, LM Studio and Msty.
Choose + / Connect an AI for any other desktop app or dedicated browser window.
The whole window is hosted; your AI keeps its existing login and conversations.
Custom desktop connections remember the executable path. Custom browser
connections also require the selected title, so reconnect if that title changes
after closing the window. Forget saved removes a connection and restores it.
Saved connections live in %LOCALAPPDATA%\AIHub\connections.json.

The usage command center uses cyan allowance rings, amber warnings and clear
remaining percentages. Only detected, enabled monitors appear, including when
only one or no monitors are available. Monitoring currently supports Codex CLI,
Grok CLI / Build and Grok Bot; connecting another app does not invent usage data.
Missing values remain unavailable, and stale readings keep their timestamps.
Other AI apps keep their own zoom controls. Existing OpenAI, Grok and Grok Bot
zoom preferences, provider settings, window recovery and updates are preserved.

0.3.1: Returning to AI Hub restores the selected AI app above its workspace
without another sidebar click. Focus restoration waits for mouse presses to
finish so sidebar clicks can complete. Other apps
stay in front while the hub is in the background. The fix preserves app sizing,
pop out, recovery and the existing update channel.

0.3.0: Public GitHub releases and automatic updates
Download: https://github.com/wernerong/ai-hub/releases/latest
Verify the ZIP with SHA256SUMS.txt, extract, and run Install-AIHub.ps1 in
PowerShell for a per-user installation with shortcuts and Windows Installed
apps registration. AIHub.exe can also run from a writable folder.

Updates check published stable releases at startup and every six hours.
The SHA-256 digest and executable version are verified before installation.
Downloaded updates install when you close AI Hub, after grouped windows are
restored. Settings offers Check for updates, Restart to update and an automatic
update switch. Failed replacement rolls back the files. Provider accounts,
zoom and usage preferences are retained. No GitHub login or Actions minutes
are needed. Earlier builds require this one manual upgrade to gain updates.

Update preferences live in %LOCALAPPDATA%\AIHub\update-settings.json; staged
updates and attempt reports live in its updates subfolder. Completed staging
is cleaned on the next app start. The updater never elevates or quits AI apps.

0.2.1: OpenAI fits above the bottom usage strip even when its standalone
minimum window height exceeds the workspace. The app content resizes to fit;
pop out restores the original size and normal minimum-size behavior.

0.2.0: Integrated usage monitoring
The compact bottom strip shows Codex, Grok CLI / Build and Grok Bot allowances.
Click an indicator or Usage in the rail for all reported windows, reset times,
credits, connection status and errors. Each strip indicator shows the most
constrained reported window. Readings refresh independently every minute.
Unavailable is never treated as a full allowance; stale readings retain their
last successful timestamp. Reset countdowns do not invent new allowance.

Settings includes provider switches, optional low-allowance and confirmed-reset
notifications, an adjustable remaining-percentage threshold, Windows startup,
and the existing app content zoom. Alerts default off. Notifications deduplicate
across restarts and resets need a fresh provider reading as confirmation.
Preferences and alert identifiers are stored in usage-settings.json under
%LOCALAPPDATA%\AIHub; the existing zoom-settings.json is preserved.

Codex usage covers Codex, not ChatGPT or Dot. Grok usage covers CLI / Build,
not Grok web chat. AI Hub uses existing local provider logins in memory to read
usage. It does not save tokens, read chat history or manage subscriptions.
The separate AI Usage Widget is unchanged; close it when using AI Hub for
monitoring to avoid duplicate provider requests. Provider API changes or expired
logins can make individual readings unavailable without interrupting other apps.

0.1.5: The compact app switcher shows each app's own icon above its name.
Icons scale with Windows display settings; descriptions remain in tooltips.

0.1.4: The left sidebar is a compact 88-pixel app switcher (at 100% Windows
scaling), down from 224 pixels. Hover over an app for its description. Settings
and the release version remain at the bottom; the active AI gets more width.

0.1.3: The release version stays visible in the title bar and sidebar.
A futuristic hub icon appears in the app, taskbar and Windows executable.

0.1.2: Settings adds saved content zoom for each AI panel. The AI Hub sidebar
and controls stay the same size. Settings hides the grouped apps while open.

Content zoom
Open Settings at the bottom of the sidebar. Choose a percentage, then select
Apply & open. Choices save automatically and apply when a panel is opened;
100% resets that app to its normal content size. Keep current zoom stops the
hub from managing that app's zoom without changing its existing size.
OpenAI and Grok Bot support 50–300% in 10% steps. Grok uses browser zoom steps
from 50–300%. OpenAI's setting is shared by ChatGPT, Codex and Dot.
Zoom uses the app's normal keyboard shortcuts. In OpenAI, an active browser
pane can receive the shortcut instead of the main chat; focus the chat area
before applying if you want to resize the main interface.
Saved choices live in %LOCALAPPDATA%\AIHub\zoom-settings.json. Shortcuts are
sent only while the intended app is focused; switching away cancels the rest.

0.1.1: Repeated clicks on the selected panel keep its app visible. Focus moves
to the selected app after the sidebar click completes. Activation alone does
not interrupt panel switching. Minimized apps restore when selected again.

Run AIHub.exe, then choose a sidebar tab:
  OpenAI: your installed ChatGPT app, including Codex and your Dot.
  Grok: your installed Grok Chrome app.
  Grok Bot: your installed Cursor Grok Bot app.

OpenAI uses its existing controls to switch between ChatGPT, Codex and Dot.
The hub does not create separate copies of those three interfaces.
If an app has multiple windows, choose the one to bring into the workspace.
Click inside the selected app to type and work normally.

Pop out returns the current app to its original window. Restore all returns
every app. Closing AI Hub restores grouped windows without quitting the apps.

How it works
AI Hub groups the original app windows visually with a shared sidebar and
workspace. It keeps them as independent top-level Windows windows, aligns the
selected one with its content area, and hides inactive windows. It does not
embed the apps as child windows, merge accounts, or replace them with model APIs.
Your existing apps continue to own their conversations, logins and subscriptions.
No API keys or account migration are needed. App-owned pop-ups may open separately.

This preview is intended for a single Windows workspace. Dragging between
monitors, virtual desktops, native app full-screen commands, voice/video,
file dialogs and accessibility tools still need broader compatibility testing.
Alt+F4 while typing in an AI app can close that original app; use the hub's
own close button to close the hub and restore all app windows.

Window recovery
The hub saves original window styles, positions and process identity in
%LOCALAPPDATA%\AIHub and starts a separate recovery helper. If the hub exits
unexpectedly, the helper restores the still-running app windows. Relaunching
the hub also attempts recovery from saved records. Usage readers use existing
local authentication tokens in memory; no chat content or passwords are read.
Never move/delete AIHub.exe while it is running.

Build from source
Requires the .NET 10 SDK on Windows. Run Build.ps1 in PowerShell.
The distributed executable includes the .NET runtime and targets Windows x64.

Automated verification
For foreground activation checks, run AIHub.exe --activation-test followed by
an absolute output folder, then click Run activation checks. Five repeated
away/return cycles use disposable windows and write activation-validation.txt.
These require a real click to acquire Windows foreground permission.

Run: AIHub.exe --self-test <absolute-test-output-directory>
This creates disposable sample windows and a report named validation.txt.
It tests grouping, bounds, switching, original window restoration, process
identity checks, crash recovery and preservation of differing display modes.
It also checks minimum-size apps in smaller workspaces and after reselection,
including restoration of their original size and minimum-size behavior.
Only the disposable sample app and the test host are closed/terminated by tests.
The release includes deterministic quota-parser, polling, cancellation, stale
data, alert deduplication and preference checks, plus the original window
recovery and zoom checks. The package includes its complete validation report.
Live verification covers the settings layout and Grok Bot zoom and reset.
OpenAI shortcut behavior was inspected in the installed app's code; its UI
was not automated. Native app updates may change their shortcut behavior.

Design references
https://learn.chatgpt.com/docs/app
https://cursor.com/docs/grok-bot
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
