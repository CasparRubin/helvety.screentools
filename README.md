# Helvety Screen Tools

WinUI 3 desktop screenshot tool for fast, keyboard-driven capture workflows on Windows.

## Development Status

This project is under active development and is not a release/stable build yet.
Expect rapid changes, incomplete features, and occasional breaking behavior while core capture UX is being refined.

## Current Focus

- Global hotkey-triggered screenshot mode
- Close-to-tray behavior (notification area) with optional full-exit-on-close setting
- Frozen-screen capture overlay
- Window snapping with visual highlight
- Free-rectangle selection
- Clipboard and save-folder capture actions
- Screenshots page with live folder refresh after new captures
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
- Settings-controlled overlay guidance visibility
- Restore main window after hidden-tray capture when at least one screenshot is saved

## Tech Stack

- .NET 8
- WinUI 3 (Windows App SDK)
- Native Win32 interop for hooks, hit-testing, and capture support
- `H.NotifyIcon.WinUI` for notification area tray integration

## Run Locally

1. Open `helvety.screentools.slnx` in Visual Studio 2022 (with WinUI/.NET desktop workloads).
2. Build and run the `helvety.screentools` project.
3. Configure save folder and hotkey in the app settings.
4. (Optional) Tune screenshot border intensity in `Settings > Screenshot Mode > Border Effects`.
5. (Optional) Choose screenshot quality mode in `Settings > Screenshot Mode > Capture Quality Mode`.
6. (Optional) Toggle screenshot overlay guidance visibility in `Settings > Screenshot Mode`.
7. (Optional) Configure close behavior in `Settings > App Behavior`:
   - Enabled (default): closing keeps the app running in the notification area so hotkeys keep working.
   - Disabled: closing fully exits the app.

## Notes

- This repository is public but still in heavy iteration.
- Quality enhancement modes improve perceived readability for some text-heavy captures, but they cannot guarantee recovery of detail that is not present in source screen pixels.
