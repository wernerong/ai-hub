AI Hub 0.1.3 — Windows desktop preview

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
the hub also attempts recovery from saved records. No chat content or passwords
are read by AI Hub. Never move/delete AIHub.exe while it is running.

Build from source
Requires the .NET 10 SDK on Windows. Run Build.ps1 in PowerShell.
The distributed executable includes the .NET runtime and targets Windows x64.

Automated verification
Run: AIHub.exe --self-test <absolute-test-output-directory>
This creates disposable sample windows and a report named validation.txt.
It tests grouping, bounds, switching, original window restoration, process
identity checks, crash recovery and preservation of differing display modes.
Only the disposable sample app and the test host are closed/terminated by tests.
The release passes 48 automated checks, including settings persistence,
per-app zoom steps, reset, invalid settings and cancellation after focus loss.
Live verification covers the settings layout and Grok Bot zoom and reset.
OpenAI shortcut behavior was inspected in the installed app's code; its UI
was not automated. Native app updates may change their shortcut behavior.

Design references
https://learn.chatgpt.com/docs/app
https://cursor.com/docs/grok-bot
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
