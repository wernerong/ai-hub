AI Hub 0.7.4 — Windows desktop workspace

0.7.4: Close connected AI windows on exit
Closing AI Hub asks all connected AI windows to close, including inactive tabs
and split panes. Each app keeps its normal save prompts and close-to-tray
behavior. Pop out, Restore all, removing an AI and switching projects release
windows to stay open independently. Restart to update and crash recovery keep
AI windows open. Provider processes are never force-terminated.

0.7.3: Taskbar mouse-up correction
Fix the remaining extra-click case when Windows briefly has no foreground
window as the taskbar button is released. Single-click minimize and restore
were verified on a secondary monitor's taskbar.

0.7.2: Single-click taskbar minimize
Click AI Hub's taskbar icon once to minimize an active hosted workspace.
Click again to restore it. Focus transitions through the taskbar or either
split pane no longer require another click. Returning from another app,
modified clicks, dragging and taskbar previews keep normal Windows behavior.

0.7.1: Stable hosted windows
Fix flickering in hosted AI windows such as OpenAI / Codex when the workspace
is smaller than the app's standalone minimum height. Idle focus maintenance
now leaves an already-fitted window's size unchanged, including in split view.
Recovery-file saves also tolerate brief Windows file locks.

0.7.0: Split view, context handoffs and local usage history
Select Split view above the workspace, choose two different AIs, then Open
split. Leave Stack vertically unchecked for side-by-side panes, or check it
for top-and-bottom panes. The cyan FOCUSED header identifies the active AI.
Click inside either app or its header to focus it. Selecting another AI in
the sidebar replaces the focused pane; selecting the AI already in the other
pane focuses that pane instead of duplicating it.

Drag the divider to save a ratio between 20% and 80%. Commands / Swap split
panes exchanges their positions. Open Split view (Layout · 2 AIs while split)
and choose Single AI to return the focused app to the full workspace. Pop out
restores the focused app's original window and expands the remaining AI.
Minimize hides both panes; restore brings back the split and focused AI.
Session resume remembers both pane apps, orientation, ratio and focus. A saved
project remembers the split when both apps are members of that project.
Unavailable apps remain named in their panes for reconnection or replacement.

Context shelf above the workspace saves titled text snippets, instructions
and file references for the active project, with a separate personal shelf.
Add text or Add files, check the items to include, and choose the target AI.
Edit Goal, Decisions, Evidence, Constraints and Next step, then review Exact
preview. Project notes are included only when you check Include project notes.
Save draft persists those five fields. Copy saves the draft and copies the
rendered handoff; Copy & open target also opens or focuses the selected AI
after copying succeeds. Paste it yourself and attach referenced files manually.
Files are references only: their contents are not read or uploaded. Nothing
is pasted or sent to another AI automatically, and chat histories are not imported.

Shelf data is local in %LOCALAPPDATA%\AIHub\context-shelf.json. Removing an item
keeps its referenced files. Deleting a project removes that project's shelf
and draft while preserving personal context and other projects. Malformed
saved data is preserved with a visible warning rather than silently replaced.

Open Usage / Usage history or search Show usage history in Commands for quota
charts, reported reset times and clearly labeled consumption estimates.
Record usage locally is enabled by default; toggle it independently, or use
Clear history to delete saved samples without changing provider allowances.
Recording uses fresh, successful numeric quota readings already collected by
the monitor and adds no provider requests. Up to 30 days and 50,000 samples are
kept, with older samples discarded. Disabling recording also pauses estimates
and retains existing history within those limits.

Usage history stores provider/quota identifiers, percentages and fetch/reset
times in %LOCALAPPDATA%\AIHub\usage-history.json. It excludes account details,
balances, tokens and chat content. Clear history after changing provider
accounts: history is local to this Windows account, not identified per provider
account. Codex, Grok CLI / Build and Grok Bot usage scopes remain separate.

An estimate needs a fresh current reading, a future reported reset, and at
least three comparable readings spanning five minutes in the same quota
window. A usage decrease, changed reset or gap over ten minutes restarts the
comparison. At most six hours of recent observations are used. Insufficient
history or no measurable consumption remains unavailable. Estimates are not
guarantees; future usage may differ. A countdown does not confirm a reset.

0.6.0: Saved projects, session resume and commands
Select Projects above the workspace, choose + New project, enter a name and
check the AI apps you want. Add optional notes, web links or full folder paths.
Save current captures the project and the hub's size, position and maximized
state. Open workspace reconnects its apps and returns to the selected AI.
Unavailable and removed apps stay saved in the project for later use.

Resume the last workspace when AI Hub starts is enabled by default. Change it
in Projects to start with the personal workspace instead. The checkbox saves
immediately, independently of project edits. The latest app selection and
window layout are remembered as you use or close AI Hub. Notes and project app
membership require Save current. Saved layouts are fitted to an available
display when restored.

Press Ctrl+Alt+Space or select Commands to search apps, projects and actions.
Use Up/Down and Enter to choose a command, or Escape to cancel. Ctrl+K also
opens commands while AI Hub itself has focus. Search for Change command
shortcut to choose Ctrl+Alt+K, Ctrl+Alt+P, Ctrl+Alt+Space or Disabled. If a
shortcut is occupied, the previous working choice remains registered.
The Commands button and in-hub Ctrl+K shortcut remain available.

Project resources open only when chosen. Copy project notes places your notes
on the clipboard for an explicit handoff; AI Hub does not read chat histories
or reopen a specific conversation inside another app. Automatic resume skips
ambiguous windows and reports unavailable apps or browsers needing reconnection.
Select the AI to finish connecting it. Installed apps with a known launcher
can be opened automatically during resume.

Projects and resume preferences live in %LOCALAPPDATA%\AIHub\workspaces.json.
The shortcut choice lives in %LOCALAPPDATA%\AIHub\command-shortcut.json.
Deleting a saved project keeps its AI apps, chats and linked folders intact.

Saved custom connections stay in the sidebar while closed unless removed.
When a browser title changes, select its sidebar button and explicitly choose
the replacement browser window. Alternatively, use + / Connect an AI, select
the open browser window and existing saved connection, then choose Reconnect
saved browser. Its name and identity stay the same, preserving project
membership without duplicates. Use a dedicated browser window: the whole
window is hosted. AI Hub does not guess unrelated browser windows or navigate
them automatically.

0.5.0: Native Windows installer and removable AI apps
Download AIHub-v0.7.3-windows-x64-Setup.exe from the latest release and
double-click it. Close AI Hub first if upgrading. Setup installs for your
Windows account with shortcuts and a native Windows Installed apps uninstaller.
No PowerShell commands, execution-policy changes or administrator access are
needed. Settings and provider apps are preserved. The installer is unsigned,
so Windows may still show an unknown-publisher or SmartScreen warning.

Right-click any AI button and select Remove from AI Hub. Its original window
is restored, and it stays out of the sidebar even if installed or running.
Use + / Connect an AI to remove apps or add a removed app back. Choices persist
in %LOCALAPPDATA%\AIHub\connections.hidden.json. App removal does not uninstall
the AI, erase its account or change independently configured usage monitors.
Claude now displays the icon from its official Windows app.

0.4.1: Normal taskbar minimize and restore
Click AI Hub's taskbar button while its connected AI is active to minimize the
workspace. Click again to restore the same selected AI. Title-bar minimization
also hides connected windows immediately. Sidebar clicks, switching back from
another app, modified clicks and dragging taskbar icons retain their behavior.
Provider windows remain independent, with their original recovery protections.

0.4.0: Your apps, discovered automatically; a new HUD usage dashboard
Only installed or open AI apps appear in the sidebar. Built-in discovery covers
OpenAI, Grok, Grok Bot, Claude, Gemini, Perplexity, DeepSeek, LM Studio and Msty.
Choose + / Connect an AI for any other desktop app or dedicated browser window.
The whole window is hosted; your AI keeps its existing login and conversations.
Custom desktop connections remember the executable path. Custom browser
connections also match the selected title; v0.6.0 adds explicit reassignment
when that title changes. Use Remove from AI Hub to hide a saved connection.
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
Use Setup.exe for installation. The ZIP is retained for automatic updates and
portable use: verify it with SHA256SUMS.txt, extract, then run AIHub.exe from a
writable folder. Legacy PowerShell scripts remain for compatibility.

Updates check published stable releases at startup and every six hours.
The SHA-256 digest and executable version are verified before installation.
Downloaded updates install when you close AI Hub. Restart to update keeps
AI windows open. Settings offers Check for updates, Restart to update and an automatic
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
every app. Closing AI Hub asks all still-connected AI windows to close.

How it works
AI Hub groups the original app windows visually with a shared sidebar and
workspace. It keeps them as independent top-level Windows windows, aligns the
one or two visible apps with their content areas, and hides other windows. It does not
embed the apps as child windows, merge accounts, or replace them with model APIs.
Your existing apps continue to own their conversations, logins and subscriptions.
No API keys or account migration are needed. App-owned pop-ups may open separately.

This preview is intended for a single Windows workspace. Dragging between
monitors, virtual desktops, native app full-screen commands, voice/video,
file dialogs and accessibility tools still need broader compatibility testing.
Alt+F4 while typing in an AI app can close that original app; use the hub's
own close button to close the hub and its connected AI windows.

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
recovery and zoom checks. Project persistence, resume preferences, command
filtering and browser reconnection are covered by additional deterministic
checks. Split layout/focus and two-window recovery, scoped context persistence,
exact handoff rendering, file-reference boundaries, local history retention
and conservative estimates have additional checks. The package includes its
complete validation report.
Live verification covers the settings layout and Grok Bot zoom and reset.
OpenAI shortcut behavior was inspected in the installed app's code; its UI
was not automated. Native app updates may change their shortcut behavior.

Design references
https://learn.chatgpt.com/docs/app
https://cursor.com/docs/grok-bot
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
