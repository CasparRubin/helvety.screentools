# Helvety Screen Tools

WinUI 3 desktop app for Windows with **screen capture** (frozen-screen selection) and **Live Draw** (full-screen overlay with a GDI bitmap of the desktop behind vector markup; on Windows 10 version 2004+ the background can refresh periodically when **capture exclusion** for the overlay succeeds—otherwise it stays a static snapshot).

## Development Status

This project is under active development and is not a release/stable build yet.
Expect rapid changes, incomplete features, and occasional breaking behavior while core capture UX is being refined.

## Current Focus

- **Two independent global hotkeys**: capture (default `Shift+S+S+S`) and Live Draw (default `Shift+D+D+D`). They must use different sequences; the app blocks applying duplicates in Settings.
- **Capture mode**: global hotkey → frozen-screen overlay with window snapping (highlighted border). **Click** (without dragging) commits the snapped window under the cursor, or the full virtual screen if nothing snaps; **drag** selects a custom rectangle. Left-click saves, copies to clipboard, and exits; right-click saves and stays in capture mode.
- **Live Draw**: global hotkey → fullscreen overlay: a **GDI bitmap** of the virtual desktop under the ink. The overlay stays **hidden** until the first frame is ready to reduce flicker. **On Windows 10 version 2004 (build 19041) and later**, when **SetWindowDisplayAffinity** capture exclusion works, the background **refreshes** periodically (~10 Hz) so the desktop can update while you draw; **on older Windows** or **if exclusion fails**, only a **static** snapshot is shown. **Default tools** (rectangle modifier **Shift**): **Shift**+drag = animated rectangles; **Ctrl+Shift**, **Alt**, or **Shift+Alt** (without Ctrl)+drag = arrows; no rectangle/arrow modifier combo = freehand. Rectangles and arrows use the same snap-border chrome as capture (gradients, dashing, pulse). **Does not require a save folder**; saving captures from capture mode still requires a writable save location.
- Close-to-tray behavior (notification area) with optional full-exit-on-close setting.
- Restore main window after capture or Live Draw when the window was tray-hidden (capture path restores after at least one save; Live Draw restores after the session ends).
- **Screen Tools** home page: new captures appear immediately when possible, with a debounced folder refresh afterward; the image grid stays visible during that refresh (no full hide while re-scanning the save folder). Optional line showing effective capture and Live Draw hotkeys when configured.
- Thumbnail previews for common image formats (PNG, JPG/JPEG, BMP, GIF, TIFF; WebP when codec is installed)
- File metadata shown as European date/time format (`dd.MM.yyyy HH:mm`) plus relative age (`... ago`)
- Built-in image editor tools (Move, Text, Border, Blur, Highlight, Arrow, Crop; Crop is last in the toolbar)
- Single-row editor settings strip with horizontal overflow handling; settings switch contextually to the active tool (or selected layer while Move is active)
- Layer list with drag-and-drop reordering (top item is rendered in front)
- Export section under Layers (bottom-right) with Save As New File, Override Existing File, Copy, and contextual Save Crop
- Arrow drawing with live preview while dragging
- Quick text re-edit via selected-layer editor controls
- Blur settings include Radius, Corner Radius, Feather, and Invert; Highlight includes Dim, Corner Radius, and Invert (inside/outside targeting)
- GPU-accelerated blur/highlight effects (Win2D-based) with interaction-first recomposition scheduling
- Editor performance optimizations: coalesced recomposition, reduced overlay churn, and deferred pixel-effect recomposition during drag/resize
- Optional `Performance Mode` toggle in editor settings to prioritize responsiveness during intensive layer editing
- Iterative UX polish (overlay guidance, animation, interaction tuning)
- Border FX personalization (intensity profile, rotating palettes, adaptive chase speed)
- Selection capture quality modes (`Fast`, `Optimized`, `Heavy`) to trade off speed vs text readability enhancement
- Settings-controlled overlay guidance visibility for **capture** mode
- Mutual exclusion between capture overlay and Live Draw via a shared gate (only one session at a time)

## Tech Stack

- .NET 8
- WinUI 3 (Windows App SDK)
- Native Win32 interop for hooks, hit-testing, and capture support
- `H.NotifyIcon.WinUI` for notification area tray integration

## Run Locally

1. Open `helvety.screentools.slnx` in Visual Studio 2022 (with WinUI/.NET desktop workloads).
2. Build and run the `helvety.screentools` project (prefer a specific **platform** such as **x64** for WinUI/MSIX packaging; **Any CPU** builds can fail packaging targets).
3. In **Settings**, choose a **save folder** (required for capture and for editing the **capture** hotkey). Configure the **capture** and **Live Draw** hotkeys as needed (Live Draw can be configured even without a save folder).
4. (Optional) Tune border intensity in `Settings > Capture Mode > Border Effects`.
5. (Optional) Choose capture quality mode in `Settings > Capture Mode > Capture Quality Mode`.
6. (Optional) Toggle capture overlay guidance visibility in `Settings > Capture Mode`.
7. (Optional) Configure close behavior in `Settings > App Behavior`:
   - Enabled (default): closing keeps the app running in the notification area so global hotkeys keep working.
   - Disabled: closing fully exits the app.

## Notes

- This repository is public but still in heavy iteration.
- Quality enhancement modes improve perceived readability for some text-heavy captures, but they cannot guarantee recovery of detail that is not present in source screen pixels.
