# Changelog

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
