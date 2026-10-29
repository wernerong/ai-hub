# Changelog

## 0.5.0 — 2026-10-29

- Add a per-user native Windows Setup.exe and native uninstaller; remove the need to run PowerShell scripts when installing a release.
- Migrate the matching legacy installation entry, preserve settings, and retain native uninstall support through automatic ZIP updates.
- Allow any detected or custom AI to be removed from the sidebar with a right-click or the connection dialog; persist removals across discovery and restarts.
- Restore removed apps through Add back to AI Hub without duplicating saved connections or uninstalling provider apps.
- Display Claude's official Windows app icon and handle brief file locks when saving connection choices.
- Validate native installation, upgrades, running-app protection and uninstall separately from the app's discovery, update and window recovery checks.

## 0.4.1 — 2026-10-28

- Minimize the workspace when its taskbar button is clicked while a hosted AI is active, and restore the same AI on the next click.
- Hide connected windows immediately on title-bar minimization and cancel queued focus restoration.
- Distinguish taskbar clicks from sidebar clicks, returns from other apps, modified clicks and taskbar drags.
- Listen for the narrow taskbar gesture on a separate message thread without intercepting input or changing native window ownership.
- Add taskbar policy and repeated native minimize/restore regression checks.

## 0.4.0 — 2026-10-28

- Discover installed or open AI apps instead of showing a fixed provider list; keep hidden hosted windows connected.
- Add a window picker and persistent custom connections for other desktop AI apps and dedicated browser windows.
- Resolve installed Windows launchers on each user's machine and provide fallback icons for additional apps.
- Redesign usage monitoring with a dark HUD, cyan remaining-allowance rings, amber warnings and responsive cards.
- Show only available, enabled usage readers; support empty and single-monitor layouts without losing hidden preferences.
- Preserve stale and unavailable readings, product-specific usage scope, window restoration and automatic public-release updates.
- Add deterministic discovery, connection persistence, browser matching, layout and icon checks to Windows validation.

## 0.3.1 — 2026-10-28

- Keep the selected AI app visible when returning to AI Hub from another window, without another sidebar click.
- Wait for mouse presses to finish before restoring app focus, and cancel the previous app's restoration when a provider or Settings is selected.
- Leave unrelated applications in front while AI Hub is in the background.
- Add an interactive regression fixture for repeated foreground activation and window ordering.

## 0.3.0 — 2026-10-27

- Publish the first public GitHub release with a self-contained Windows x64 package.
- Add automatic updates from published stable AI Hub releases. Check at startup and every six hours; verify the GitHub SHA-256 digest before staging.
- Apply updates after the hub restores app windows and exits. Offer an immediate restart in Settings and an automatic-update switch.
- Restore replaced files if installation fails. Keep provider logins, usage preferences, zoom settings and unrelated files intact.
- Include a per-user installer, Desktop and Start menu shortcuts, and Windows Installed apps registration.
- Build, test, tag and verify release assets locally without GitHub Actions minutes.

## 0.2.1

- Fit OpenAI windows above the usage strip even when their normal minimum size exceeds the available space.

## 0.2.0

- Add the Usage page, compact allowance strip, provider controls, optional alerts and Windows startup setting.
- Preserve stale readings and confirm resets using fresh provider readings.

## 0.1.5

- Show each provider's icon in the compact sidebar.

## 0.1.3

- Add saved app content zoom, visible release version, branding, and native window restoration.
