# AI Hub roadmap

The next releases build on the existing native Windows workspace and public update channel. Version numbers below describe the intended sequence, not release dates. Each phase is validated before publication; builds and release uploads continue locally without GitHub Actions minutes.

## Foundation — v0.5.0

The published v0.5.0 release adds native per-user installation and uninstall, persistent removal and restoration of sidebar apps, and upgrade compatibility. Earlier releases provide app discovery, usage monitoring, native window recovery and normal taskbar minimize/restore behavior.

## Project continuity — v0.6.0

- Save named projects with AI apps, notes, links, folders and the hub's window layout.
- Resume the last workspace and selected AI, with an independent startup preference.
- Search available projects, apps and actions through a command palette and configurable shortcut.
- Reconnect changed browser windows explicitly while keeping saved connection identities and project membership.

Project data stays local. Hosting a native window does not grant access to its conversations. Resume and project opening leave ambiguous window assignments to the user.

## Working across AIs — planned v0.7.0

- Two-AI split view with saved layouts and a clear focused pane.
- A context shelf for explicitly saved snippets, files and project instructions.
- Editable handoff templates containing the goal, decisions, evidence, constraints and next step.
- Local usage history, reset timelines and clearly labeled consumption estimates, retaining each provider's actual product scope.

Split view must extend minimize/restore, focus, recovery and display-scaling checks before release. Context transfer starts with content the user chooses; automatic sending or chat imports require a supported provider integration. The shelf must show what will be shared and where before a handoff is sent.

## Public product polish — planned v1.0

- Capability-based integrations that expose only supported actions, usage, links, imports and notifications.
- Optional voice commands for workspace navigation and status, with explicit microphone controls.
- Consistent command-centre styling, purposeful state transitions and reduced-motion support.
- Broader compatibility validation across sleep/wake, monitor changes, virtual desktops and provider updates.
- Trusted code signing for the native installer and executable once a signing identity is available, plus documented release provenance and upgrade recovery.

The current installer remains unsigned until that signing work is configured and verified. Deeper integrations and voice controls require their own capability, permission and compatibility checks; they are not part of v0.6.0.
