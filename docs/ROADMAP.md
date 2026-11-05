# AI Hub roadmap

AI Hub builds on its native Windows workspace and public update channel. The
phases below describe implemented capabilities through v0.7.0. Builds,
validation and release uploads run locally without GitHub Actions minutes.

## Foundation — v0.5.0

Native per-user installation and uninstall, persistent removal and restoration
of sidebar apps, and upgrade compatibility build on app discovery, usage
monitoring, native window recovery and taskbar minimize/restore behavior.

## Project continuity — v0.6.0

- Named projects save AI apps, notes, links, folders and window layout.
- Session resume restores the last workspace, with an independent startup preference.
- A command palette finds available projects, apps and actions through a configurable shortcut.
- Explicit browser reassignment preserves saved connection identities and project membership.

Project data stays local. Ambiguous window assignments remain a user choice.
Native window hosting does not grant access to provider conversations.

## Working across AIs — v0.7.0

- Two-AI split view supports side-by-side and stacked panes, clear focus, a saved divider ratio and pane swapping. Projects and sessions preserve the layout; popping out one app expands the remaining app.
- A context shelf stores user-selected snippets, instructions and file references separately for each project and the personal workspace.
- Editable handoff drafts contain Goal, Decisions, Evidence, Constraints and Next step. An exact preview precedes explicit copying; opening the chosen target does not paste or send the handoff. Files require manual attachment.
- Local usage history stores fresh numeric observations for up to 30 days and 50,000 samples. Product-specific reset timelines and labeled consumption estimates retain their source scopes and show insufficient history when appropriate.
- Local data has bounded persistence and malformed-file protection. Shelf removal never deletes referenced files; history can be disabled or cleared independently of provider allowances.

These features use the existing native Setup.exe, portable/update ZIP and public
release updater. Provider accounts and conversations stay in their own apps.

## Planned v1.0 — public product polish

This is the next planned phase, not functionality included in v0.7.0. It has
no committed release date and requires its own implementation and validation.

- Capability-based integrations that expose only supported actions, usage, links, imports and notifications.
- Optional voice commands for workspace navigation and status, with explicit microphone controls.
- Consistent command-centre styling, purposeful transitions and reduced-motion support.
- Broader compatibility validation across mixed-DPI monitors, monitor changes, sleep/wake, virtual desktops and provider updates.
- Trusted signing for the native installer and executable once a signing identity is available, with documented release provenance and recovery.

The current installer remains unsigned. Voice control, automatic conversation
imports and automatic handoff sending are not implemented. Release validation
must continue to preserve focus, minimize/restore and original-window recovery
as compatibility work expands.
