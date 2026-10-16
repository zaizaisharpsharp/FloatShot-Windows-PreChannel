# FloatShot v0.7.2 — Updates UI

This builds on the compact floating screenshot experience in v0.7.1. The updater has been redesigned so you never need to enter a GitHub release source manually.

## Updated tray menu

Right-click the FloatShot icon near the clock, then open **Updates (v0.7.2)**:

- **Check for Updates...** opens a compact panel showing **Installed version** and **Latest available**.
- **Check Again** fetches the current release metadata from the official FloatShot update channel.
- **Update Now** becomes available only when a newer published release is found. Selecting it downloads the app, verifies the published SHA-256 checksum, stages the update, then installs and restarts FloatShot. If screenshot/editor windows are open, installation waits until they are closed.
- **Automatically Check for Updates** is optional, enabled by default. It checks on startup and roughly every six hours; checking never installs a new version without your click.
- **Update Log** opens local diagnostic history.

**Removed:** **Set GitHub Release Source** and the repository entry prompt. The update channel is now hardcoded to `classifiedamazing/FloatShot-Releases`, which must remain public to serve unauthenticated releases. Old `Repository` settings are ignored.

**No public release is available yet.** Until the first Windows build is validated and a versioned ZIP plus manifest are published, the updater will show **Latest available: Not published** rather than pretend an update is ready. The source repository `classifiedamazing/FloatShot-Windows` remains private.

## Install v0.7.2

1. Quit the previously running FloatShot from its tray menu.
2. Extract the downloaded source ZIP.
3. Run `INSTALL-WINDOWS.bat` inside the extracted `FloatShot-Windows-v7.2` folder.
4. When the build succeeds, FloatShot launches automatically and will subsequently start with Windows.

The installer builds the replacement before installing it. Previous captures remain in `Desktop\FloatShot Captures`, and existing settings are preserved.

## Existing screenshot features

Area, full screen, active window, current monitor, timed capture, compact hover-only floating screenshots, a six-card vertical stack, drag-and-drop, Copy / Ctrl+V, immediate Desktop storage, rename, and editing (pen, highlight, redact, circle, rectangle, arrows, text).

**Verification note:** The code and archive were checked here, but there is no Windows WPF compiler in this environment. Windows compilation and live end-to-end update installation still require testing. GitHub Actions is currently blocked by the GitHub account billing/spending restriction.