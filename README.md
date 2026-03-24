# Helvety Screen Tools

WinUI 3 desktop app for Windows with **screen capture** (frozen-screen selection) and **Live Draw** (full-screen **layered** overlay: a **GDI chroma-key** fill plus **transparent** WinUI ink so the real desktop—including video—shows through; WinUI’s compositor ignores `LWA_COLORKEY` on opaque fills, so this is not a simple black color key). Ink is **WinUI XAML**, not ZoomIt’s off-screen GDI buffer. Live Draw does **not** use a GDI BitBlt **snapshot** of the desktop or a refresh loop.

## Development Status

The app can be **packaged and deployed** (MSIX or unpackaged). Behavior and defaults may still **change between releases**; see **Notes** below when upgrading or supporting users.

## Current Focus

- **Two independent global hotkeys**: capture (default `Shift+S+S+S`) and Live Draw (default `Shift+D+D+D`). They must use different sequences; the app blocks applying duplicates in Settings.
- **Settings layout**: the main **NavigationView** has a single left rail. **Settings** is an expandable group (PowerToys-style) with **General**, **Screen capture**, and **Live Draw**. **General** covers close-to-tray, editor performance, global **snap-border intensity** (Subtle / Balanced / Bold) for both frozen-screen capture and Live Draw, and a **Reset all settings** action. **Screen capture** includes save folder, master **On/Off**, hotkey editor, capture quality, and overlay instructions. **Live Draw** includes a master **On/Off** toggle (shortcuts stay stored when off), global hotkey editor, and **Shape shortcuts** (per-tool modifiers plus fixed free draw and sparkle).
- **Frozen-screen capture** (global hotkey): overlay with window snapping (highlighted border). **Esc** cancels and closes the overlay. **Click** (without dragging) commits the snapped window under the cursor, or the full virtual screen if nothing snaps; **drag** selects a custom rectangle. Left-click saves, copies to clipboard, and exits; right-click saves (no clipboard copy) and stays in the same capture session.
- **Live Draw**: global hotkey → fullscreen **Win32 layered host** hosting a **DesktopWindowXamlSource** island (vector markup only; no desktop BitBlt). **Esc** ends the session. **Settings → Live Draw → Shape shortcuts** assigns **Shift**, **Ctrl**, **Alt**, **Win**, or **None** (unbound) to **Rectangle**, **Arrow**, and **Straight line** (left mouse; each non-None modifier must be unique) and to **Circle** and **Ellipse** (right drag; must differ unless one is **None**). Left drag with **no** matching shape modifier is **free draw**; right click and hold with **no** circle/ellipse modifier is the **sparkle** animation (fixed). **Rectangles, ellipses, circles, arrows, straight lines, freehand, and sparkle** use the same snap-border chrome as capture selection (gradients, dashing, pulse). **Does not require a save folder**; saving captures from capture mode still requires a writable save location.
- **Navigation**: left **NavigationView** with **General** (home), an expandable **Settings** group (**General**, **Screen capture**, **Live Draw**), and **About**. **About** shows a short product summary, a compile-time **Build Version** (`v0.yyMMdd.HHmm.ss` from local clock when the project is built; see `GenerateAppBuildStamp` in `helvety.screentools.csproj`), and links to [helvety.com](https://helvety.com/) and the [GitHub repository](https://github.com/CasparRubin/helvety.screentools).
- Close-to-tray behavior (notification area) with optional full-exit-on-close setting.
- Restore main window after capture or Live Draw when the window was tray-hidden (capture path restores after at least one save; Live Draw restores after the session ends).
- **General** home page (titled **Helvety Screen Tools**): after each save, the coordinator notifies this page so the gallery **reloads** (same as navigating back to home). For **`.png`** files thumbnails use a **scaled decode from disk** (about max width 520px), then a generic file decode, then the **shell thumbnail** provider. A **debounced** `FileSystemWatcher` still runs a full folder rescan so ordering and metadata stay correct. **Listing files** for a refresh runs on a **background thread** before the gallery lock; applying updates to the grid stays **serialized**. Thumbnails attach to the **visible row by file path**. In-flight thumbnail work is **not** cancelled on every refresh (only when leaving the page). Under the **Images** heading, the UI reminds you that **left-click** opens the image in the editor and **right-click** copies the image to the clipboard.
- Thumbnail previews for common image formats (PNG, JPG/JPEG, BMP, GIF, TIFF; WebP when a codec is installed)
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
- **Snap-border overlay** (frozen-screen capture + Live Draw): shared animated chrome (gradients, dashing, pulse). Global **intensity** (Subtle / Balanced / Bold) is under **Settings → General**. The **image editor** uses separate stroke and effect controls for layers.
- Selection capture quality modes (`Fast`, `Optimized`, `Heavy`) to trade off speed vs text readability enhancement; **default is `Fast`** for responsive saves. **`Heavy`** applies large upscales and filters and can take several seconds on big selections—use it when you explicitly want maximum enhancement.
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
2. Build and run the `helvety.screentools` project. The project **defaults to x64** when no platform is set so plain `dotnet build` works; you can still pass `-p:Platform=x86` or `ARM64` explicitly. (**Any CPU** is remapped to x64 because WinUI/MSIX packaging cannot target neutral architecture.) Each build regenerates the **About** page **Build Version** (`v0.yyMMdd.HHmm.ss`).
3. Use the navigation pane to switch between **General**, **Settings** (expand for section items), and **About**.
4. Under **Settings**, choose a section:
   - **General**: **Notification area** (close-to-tray), **Editor performance mode**, and **Snap border** intensity (global for capture selection and Live Draw).
   - **Screen capture**: **Save location** (required for frozen-screen capture), master toggle, **Listen**-based hotkey editor (changes auto-save), **Capture quality**, and **Overlay** instructions.
   - **Live Draw**: master toggle, **Listen**-based hotkey editor (changes auto-save), and **Shape shortcuts** (per-tool modifiers). **Live Draw** can be configured without a save folder.
   - **General** also includes a **Reset all settings** action (captures on disk are not deleted).
5. (Optional) Under **General → Notification area**, choose whether closing the window keeps the app in the notification area (default) or fully exits.

## Notes

- **About** Build Version: the value shown in **About** is written at **compile time** (not runtime). Rebuild the project to refresh it; it reflects the **local** machine clock in `v0.yyMMdd.HHmm.ss` form.
- **Capture quality** defaults to **Fast** for new settings profiles (missing `ScreenshotQualityMode` in LocalSettings). If you previously saved **Heavy** or **Optimized**, that choice is kept until you change it under **Settings → Screen capture → Capture quality**.
- Default save folder when the app first creates one (and you have not cleared it in Settings) is **`Helvety Screen Tools captures`** on your desktop (see `SettingsService`). The top-nav home item is labeled **General** and the page title is **Helvety Screen Tools**; this is not tied to any legacy “Screenshots” folder name.
- Quality enhancement modes improve perceived readability for some text-heavy captures, but they cannot guarantee recovery of detail that is not present in source screen pixels.
