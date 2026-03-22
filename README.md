# Helvety Screen Tools

WinUI 3 desktop app for Windows with **screen capture** (frozen-screen selection) and **Live Draw** (full-screen **layered** overlay: a **GDI chroma-key** fill plus **transparent** WinUI ink so the real desktop—including video—shows through; WinUI’s compositor ignores `LWA_COLORKEY` on opaque fills, so this is not a simple black color key). Ink is **WinUI XAML**, not ZoomIt’s off-screen GDI buffer. Live Draw does **not** use a GDI BitBlt **snapshot** of the desktop or a refresh loop.

## Development Status

This project is under active development and is not a release/stable build yet.
Expect rapid changes, incomplete features, and occasional breaking behavior while core capture UX is being refined.

## Current Focus

- **Two independent global hotkeys**: capture (default `Shift+S+S+S`) and Live Draw (default `Shift+D+D+D`). They must use different sequences; the app blocks applying duplicates in Settings.
- **Capture mode**: global hotkey → frozen-screen overlay with window snapping (highlighted border). **Esc** cancels and closes the overlay. **Click** (without dragging) commits the snapped window under the cursor, or the full virtual screen if nothing snaps; **drag** selects a custom rectangle. Left-click saves, copies to clipboard, and exits; right-click saves (no clipboard copy) and stays in capture mode.
- **Live Draw**: global hotkey → fullscreen **Win32 layered host** hosting a **DesktopWindowXamlSource** island (vector markup only; no desktop BitBlt). **Esc** ends the session. **Left-mouse** shape tools (Rectangle modifier **Shift** by default): **Shift**+drag = rectangles; **Alt**+drag (without Ctrl) = arrows; **Ctrl**+drag = straight lines (uniform stroke, not arrow-shaft taper; no arrowhead); drag without a matching shape-tool modifier = freehand. **Other Rectangle modifier** options in Settings remap which modifier selects rectangles vs arrows vs straight lines; if the modifier is **Ctrl**, **Ctrl**+drag is rectangles only (straight line via Ctrl is disabled). **Right-mouse** shortcuts are **fixed** (they do **not** follow Rectangle modifier): plain **right-click** = sparkle at the pointer; **Shift**+right drag = circle; **Alt**+right drag (without Ctrl) = ellipse (if both Shift and Alt are held, Shift+right wins). **Rectangles, ellipses, circles, arrows, straight lines, freehand, and sparkle** use the same snap-border chrome as capture selection (gradients, dashing, pulse). **Does not require a save folder**; saving captures from capture mode still requires a writable save location.
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
- Native Win32 interop (global hotkeys, window hit-testing, GDI capture for selection overlay, layered host for Live Draw)
- Self-contained Windows App SDK runtime (unpackaged `dotnet run` / F5 without a separate machine-wide Windows App Runtime install)
- `H.NotifyIcon.WinUI` for notification area tray integration

## Run Locally

1. Open `helvety.screentools.slnx` in Visual Studio 2022 (with WinUI/.NET desktop workloads).
2. Build and run the `helvety.screentools` project. The project **defaults to x64** when no platform is set so plain `dotnet build` works; you can still pass `-p:Platform=x86` or `ARM64` explicitly. (**Any CPU** is remapped to x64 because WinUI/MSIX packaging cannot target neutral architecture.)
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
