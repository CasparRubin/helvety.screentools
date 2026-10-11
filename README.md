# Helvety Screen Tools

WinUI 3 desktop app for Windows with **screenshot** (frozen-screen selection) and **Live Draw** (full-screen overlay: a **`WS_EX_NOREDIRECTIONBITMAP`** Win32 host with **`DwmExtendFrameIntoClientArea`** and a **transparent** WinUI XAML island so the real desktop—including video—shows through with zero color shift). Ink is **WinUI XAML**, not ZoomIt’s off-screen GDI buffer. Live Draw does **not** use a GDI BitBlt **snapshot** of the desktop or a refresh loop.

## Development Status

The app can be **packaged and deployed** (MSIX or unpackaged). Behavior and defaults may still **change between releases**; see **Notes** below when upgrading or supporting users.

## Current Focus

- **Two independent global hotkeys**: screenshot (default `Shift+S+S+S`) and Live Draw (default `Shift+D+D+D`). Each sequence can use **up to five keys** (defaults use three). They must use different sequences; the app blocks applying duplicates in Settings.
- **Settings layout**: the main **NavigationView** has a single left rail. **Settings** is an expandable group (PowerToys-style) with **General**, **Screenshot**, and **Live Draw**. **General** covers close-to-tray, **Global hotkey listeners** (master on/off for screenshot and Live Draw shortcuts; when off, the home page shows a warning with a link to settings), **Run at sign-in** (labeled **Start with Windows** in the app; Windows **Startup** apps; packaged MSIX only—hidden when running unpackaged; **on by default** for new packaged installs), **Editor performance mode**, global **snap-border intensity** (Subtle / Balanced / Bold; **default Bold**) for both screenshot and Live Draw, and **Reset all settings** (restores every stored preference to defaults, including hotkeys, listeners, editor toolbar defaults, Live Draw shape modifiers, screenshot quality, save folder to the default desktop **Helvety Screen Tools captures** folder, and **Run at sign-in**; packaged builds may re-request startup enablement; does not delete image files on disk). **Screenshot** includes save folder, master **On/Off**, hotkey editor, screenshot quality, and overlay instructions. **Live Draw** includes a master **On/Off** toggle (shortcuts stay stored when off), global hotkey editor, **Line thickness**, **Shape shortcuts** (per-tool modifiers plus fixed free draw), and a **Sparkle** on/off toggle (right-hold animation when enabled).
- **Screenshot** (global hotkey): overlay with window snapping (highlighted border). **Esc** cancels and closes the overlay. **Click** (without dragging) commits the snapped window under the cursor, or the current monitor under the cursor if nothing snaps; **drag** selects a custom rectangle. Left-click saves, copies to clipboard, and exits; right-click saves (no clipboard copy) and stays in the same screenshot session.
- **Live Draw**: global hotkey → fullscreen **Win32 `WS_EX_NOREDIRECTIONBITMAP` host** with a **DesktopWindowXamlSource** island (vector markup only; no desktop BitBlt). **Esc** ends the session. **Settings → Live Draw → Line thickness** controls the stroke width for rectangles/ellipses/freehand arrows and straight-line ink. **Settings → Live Draw → Shape shortcuts** assigns **Shift**, **Ctrl**, **Alt**, **Win**, or **None** (unbound) to **Rectangle**, **Arrow**, and **Straight line** (left mouse; each non-None modifier must be unique) and to **Circle** and **Ellipse** (right drag; must differ unless one is **None**). Left drag with **no** matching shape modifier is **free draw** (no modifier picker). Right click and hold with **no** circle/ellipse modifier runs the **sparkle** animation when **Sparkle** is enabled in settings (modifier assignment for sparkle is not configurable). **Rectangles, ellipses, circles, arrows, straight lines, freehand, and sparkle** use the same snap-border chrome as screenshot selection (gradients, dashing, pulse). **Does not require a save folder**; saving screenshots from screenshot mode still requires a writable save location.
- **Navigation**: left **NavigationView** with **General** (home), an expandable **Settings** group (**General**, **Screenshot**, **Live Draw**), and **About**. **About** shows a short product summary, the app **Version** (package version when installed; assembly version when running unpackaged), and links to [helvety.com](https://helvety.com/) and the [GitHub repository](https://github.com/CasparRubin/helvety.screentools).
- Close-to-tray behavior (notification area) with optional full-exit-on-close setting. The tray icon context menu offers **Open**, **Enable/Disable global hotkey listeners**, and **Exit**. When the packaged app starts from the Windows **startup task**, it can launch **hidden to the tray** instead of showing the main window.
- Restore main window after **screenshot** when the window was tray-hidden and **at least one screenshot was saved** in that session (Live Draw does not restore the main window).
- **General** home page (titled **Helvety Screen Tools**): after each **screenshot** is saved to the configured folder, the coordinator notifies this page so the gallery **reloads** (same as navigating back to home). Other saves (for example from the editor) rely on the folder watcher and debounced rescan like any file change. For **`.png`** files thumbnails use a **scaled decode from disk** (about max width 520px), then a generic file decode, then the **shell thumbnail** provider. A **debounced** `FileSystemWatcher` still runs a full folder rescan so ordering and metadata stay correct. **Listing files** for a refresh runs on a **background thread** before the gallery lock; applying updates to the grid stays **serialized**. Images are grouped into date sections (**Today**, **Yesterday**, **Last Week**, **Older**). Thumbnails attach to the **visible row by file path**. In-flight thumbnail work is **not** cancelled on every refresh (only when leaving the page). The images header sits above the card and reminds you that **left-click** opens the image in the editor, **right-click** copies the image to the clipboard, and the **bin icon** deletes the file to the Recycle Bin.
- Home gallery lists **PNG** captures from the save folder only (screenshot saves are PNG; other extensions in the folder are ignored)
- File metadata shown as European date/time format (`dd.MM.yyyy HH:mm`) plus relative age (`... ago`)
- Built-in **PNG** image editor in a **separate window** from the home gallery (one window per file path); tools: **Canvas**, Text, Border, Blur, Highlight, Arrow, Magnifier, Crop, Resize, Color (Color is last in the toolbar)
- Single-row editor settings strip with horizontal overflow handling; settings switch to the active tool, or to the selected layer's settings while **Canvas** is active. Toolbar defaults (colors, thickness, toggles, etc.) are **persisted quietly** so the main window and gallery are not refreshed on every change (keeps the editor window in focus)
- **Canvas** tool: select, move, and resize layers (resize handles on the selection). When no layer-specific panel applies, the **Size** card controls viewport zoom (slider, +/-, **Reset** to fit the image to the editor width; zoom is per session, not saved in settings)
- Layer list with drag-and-drop reordering (top item is rendered in front). **Delete** or **Backspace** removes the selected layer (from the canvas or layer list) or clears an active crop region; while the inline text editor is focused, those keys edit text instead
- Mouse wheel over the canvas adjusts the selected layer's primary size (text size, arrow/border/magnifier thickness, blur radius, highlight dim, or magnifier zoom); **Ctrl**+wheel still zooms the viewport
- Export section under Layers (bottom-right) with **Save, copy and close** and **Save and close** (**Save crop...** while a crop is active, **Save resized...** while Resize scale is under 100%); each save writes a new flattened PNG and closes the editor, while the first option also copies the result to clipboard
- Saved edits are intentionally non-reeditable: reopening a saved image starts from the bitmap result, and new annotations can be added in a fresh edit session
- Arrow tool: **Tapered** or **Straight** forms with optional border and shadow. New arrows are straight; a selected arrow has a **middle handle** you can drag to bow the shaft. While drawing, only the arrow preview on the overlay updates (existing layers are not rebuilt on every pointer move)
- **Magnifier** tool: drag a circle or rectangle source region; a larger loupe is placed nearby with nearest-neighbor upscaled pixels and a tapered connector from source to loupe. Source and loupe move independently; sizes stay linked by zoom
- **Resize** tool: shrink the exported PNG only (scale **10%–100%**, never enlarge). The canvas and layers stay at the original pixel size. The settings strip shows the output dimensions immediately and, after a short pause, the real saved PNG size (source file size → encoded size, for example `4.3 MB → 980 KB`). **Max size** (KB) and **Fit** pick the largest scale whose encoded PNG is at or under that limit; if 10% is still over, the slider stays at 10% and the readout shows the actual size. Save crops first when a crop is set, then downscales. A scaled export is named `*_resized.png` (including crop + scale); crop-only stays `*_crop.png`.
- **Color** tool: click a pixel to sample **hex** (`#RRGGBB`) and **RGB**; copy either to the clipboard. Stays selected so you can keep sampling. Reads the composed image (screenshot plus baked blur/highlight), not vector overlays.
- **Text** tool: **click** to place a resizable text box (default ~380×100 px) or **drag** to size first; while typing, drag the frame border to move or use corner/edge handles to resize (full edge strips, not just corners); **bold** and **italic** styling; re-edit via toolbar controls or **double-click** an existing text layer
- Blur settings include Radius, Corner Radius, Feather, and Invert; Highlight includes Dim, Corner Radius, and Invert (inside/outside targeting)
- **Blur** and **Highlight** bake into the base image through **`EditorComposePipeline`** (Win2D **GPU** per-region ROI when available, **CPU** fallback otherwise). GPU blur normalizes each ROI to image coordinates before masking so the blurred area matches the selection. If GPU compositing fails, the editor uses CPU for the **rest of that editor session** and may show a one-time warning toast; **`GpuEffectsEnabled`** is stored only when you change and save toolbar defaults (a GPU failure alone does not turn GPU off for future sessions unless you save settings afterward)
- While moving or resizing layers, selection handles update on the overlay and full layer recomposes wait until pointer release (blur/highlight regions do not live-preview during the drag)
- Editor responsiveness: coalesced recomposes, generation tokens that cancel stale GPU work when sliders change quickly, and reused vector visuals on the layer canvas between edits
- Optional **Editor performance mode** under **Settings → General**: skips shadows and other expensive vector effects during drag, resize, and arrow preview (blur and highlight still refresh after you release the pointer). Applies to **open editor windows** immediately without stealing focus
- **Snap-border overlay** (screenshot + Live Draw): shared animated chrome (gradients, dashing, pulse). Global **intensity** (Subtle / Balanced / Bold) is under **Settings → General**. The **image editor** uses separate stroke and effect controls for layers.
- Selection screenshot quality modes (`Fast`, `Optimized`, `Heavy`) to trade off speed vs text readability enhancement; **default is `Fast`** for responsive saves. **`Heavy`** applies large upscales and filters and can take several seconds on big selections—use it when you explicitly want maximum enhancement.
- Settings-controlled overlay guidance visibility for **screenshot** mode
- Mutual exclusion between screenshot overlay and Live Draw via a shared gate (only one session at a time)

## Tech Stack

- .NET 10 (`net10.0-windows10.0.19041.0`)
- WinUI 3 via **Windows App SDK 2.5.1** (see `Microsoft.WindowsAppSDK` in `helvety.screentools.csproj`)
- Native Win32 interop (global hotkeys, window hit-testing, GDI freeze-frame for selection overlay, `WS_EX_NOREDIRECTIONBITMAP` host for Live Draw)
- Self-contained Windows App SDK runtime (unpackaged `dotnet run` / F5 without a separate machine-wide Windows App Runtime install)
- `H.NotifyIcon.WinUI` 2.4.1 for notification area tray integration
- `Microsoft.Graphics.Win2D` and **CommunityToolkit.WinUI** settings controls

## Install

### Download from GitHub Releases (recommended)

Go to the [**Releases**](https://github.com/CasparRubin/helvety.screentools/releases) page, download the ZIP for your platform (`helvety-screentools-v<tag>-win-x64.zip` or `helvety-screentools-v<tag>-win-arm64.zip`), extract it anywhere, and run **`helvety.screentools.exe`**. The app is self-contained — no .NET or Windows App Runtime install required.

### Build from source

Paths below are **relative to the repository root** (where `helvety.screentools.slnx` lives). Replace `<Platform>` with `x64`, `x86`, or `ARM64`, and `<Configuration>` with `Debug` or `Release`.

#### Run unpackaged (quickest)

From the repo root:

```bash
dotnet run --project helvety.screentools/helvety.screentools.csproj -p:Platform=<Platform>
```

Or build, then start the executable:

```bash
dotnet build helvety.screentools/helvety.screentools.csproj -c <Configuration> -p:Platform=<Platform>
```

The app is **`helvety.screentools.exe`**, under:

`helvety.screentools/bin/<Platform>/<Configuration>/` → open the **one** folder whose name starts with **`net`** and ends with **`windows`** → then the runtime folder **`win-x64`**, **`win-x86`**, or **`win-arm64`** (matching your platform).

**Visual Studio:** use the **helvety.screentools (Unpackaged)** launch profile (see `helvety.screentools/Properties/launchSettings.json`).

Unpackaged runs **do not** register the app as a full MSIX install; **Settings → Apps → Startup** integration and **Run at sign-in** in the app apply to the **packaged** build only.

#### Build an MSIX package

From the repo root (example: **Release**, **x64**):

```bash
dotnet msbuild helvety.screentools/helvety.screentools.csproj -p:Configuration=Release -p:Platform=x64 -t:Publish -p:GenerateAppxPackageOnBuild=true -p:SignMsix=true
```

The **`.msix`** file is written under **`helvety.screentools/AppPackages/`**, inside a subfolder named for the app, version, and platform (open that folder and use the `.msix` you find there).

#### Install the MSIX

- **Double-click** the `.msix` and confirm the installer, or from PowerShell:  
  `Add-AppxPackage -Path "<path-to-your>.msix"`

**Publisher:** the MSIX manifest uses **Helvety** as `PublisherDisplayName` and `CN=Helvety` as the package identity publisher (must match the dev signing certificate).

**Signing (first time on a machine):** run `.\scripts\EnsureMsixDevCert.ps1` from the repo root. That creates `helvety.screentools_TemporaryKey.pfx` (password `helvety-dev`, referenced in the `.csproj`) and imports it into your user certificate store.

**Signing and trust:** packages are signed with that **self-signed** dev certificate. Windows may refuse to install until you either:

- enable **Developer Mode** and use **`Add-AppxPackage -AllowUnsigned`** only when your package is in the correct **unsigned** publisher configuration, or  
- **sign** the `.msix` with a certificate whose chain is trusted (typical for self-signed dev certs: import the **signer** into **Local Computer → Trusted Root Certification Authorities** using an **elevated** session, then install).

If Windows reports that an **unpackaged** registration of the same app already exists, remove that app (Settings → Installed apps, or `Get-AppxPackage` / `Remove-AppxPackage`) before installing the MSIX.

**Visual Studio:** use **helvety.screentools (Package)** or the project’s **Package and Publish** flow; output still lands under **`helvety.screentools/AppPackages/`**.

## Run locally

Use **Install** above for `dotnet run`, unpackaged **exe** locations, and **MSIX** output. The steps below focus on the **Visual Studio** workflow and **Settings** tour.

1. Open `helvety.screentools.slnx` in Visual Studio 2022 or newer (with WinUI / .NET desktop workloads).
2. Build and run the `helvety.screentools` project. The project **defaults to x64** when no platform is set so plain `dotnet build` works; you can still pass `-p:Platform=x86` or `ARM64` explicitly. (**Any CPU** is remapped to x64 because WinUI/MSIX packaging cannot target neutral architecture.) The shipped **version** is defined in `helvety.screentools.csproj` (`ApplicationVersion`, `ApplicationDisplayVersion`, and `Version`, currently aligned with `Package.appxmanifest` / `app.manifest`); bump those together for each release. **About** shows the package version when installed (MSIX) or the assembly version when running unpackaged (`dotnet run` / F5).
3. Use the navigation pane to switch between **General**, **Settings** (expand for section items), and **About**.
4. Under **Settings**, choose a section:
   - **General**: **System tray** (close-to-tray), **Global hotkey listeners**, **Run at sign-in** (when packaged), **Editor performance mode** (lighter vector effects while dragging; blur/highlight refresh after pointer release; applies to open editor windows), **Snap border** intensity (global for screenshot and Live Draw), and **Reset all settings** (see **Current Focus** for scope).
   - **Screenshot**: **Save location** (required for screenshot), master toggle, **Listen**-based hotkey editor (changes auto-save), **Screenshot quality**, and **Overlay** instructions.
   - **Live Draw**: master toggle, **Listen**-based hotkey editor (changes auto-save), **Line thickness**, **Shape shortcuts** (per-tool modifiers), and **Sparkle** on/off. **Live Draw** can be configured without a save folder.
5. (Optional) Under **General → System tray**, choose whether closing the window keeps the app in the notification area (default) or fully exits.

## Releasing

A [GitHub Actions workflow](.github/workflows/release.yml) automates builds and GitHub Releases. To publish a new version:

1. Bump the version in `helvety.screentools.csproj` (`ApplicationDisplayVersion`, `ApplicationVersion`, and `Version`), `Package.appxmanifest`, and `app.manifest` so they stay aligned.
2. Commit the version bump.
3. Tag and push:

```bash
git tag v2.6.0.0
git push origin v2.6.0.0
```

Use a tag that matches the four-part `ApplicationVersion` in `helvety.screentools.csproj` (for example `v2.6.0.0`). **About** shows the full four-part version when installed (MSIX) or unpackaged.

The workflow builds self-contained portable ZIPs for **x64** and **arm64** (`helvety-screentools-v<tag>-win-x64.zip` and `helvety-screentools-v<tag>-win-arm64.zip`), then creates a GitHub Release with auto-generated release notes and the ZIPs attached. No certificate or MSIX signing is needed for the portable distribution.

## Notes

- **Automated tests**: the solution currently has **no** `dotnet test` project; validation is by **build** (`dotnet build helvety.screentools.slnx` or the `.csproj` with `-c Release` and the desired `-p:Platform=`).
- **About → Version**: **installed MSIX** shows `Package.Current` identity (matches release versioning). **Unpackaged** runs show the assembly version from the same `Version` MSBuild property.
- **Screenshot quality** defaults to **Fast** for new settings profiles (missing `ScreenshotQualityMode` in LocalSettings). If you previously saved **Heavy** or **Optimized**, that choice is kept until you change it under **Settings → Screenshot → Screenshot quality**.
- Default save folder when the app first creates one (and you have not cleared it in Settings) is **`Helvety Screen Tools captures`** on your desktop. The top-nav home item is labeled **General** and the page title is **Helvety Screen Tools**; this is not tied to any legacy “Screenshots” folder name.
- Quality enhancement modes improve perceived readability for some text-heavy screenshots, but they cannot guarantee recovery of detail that is not present in source screen pixels.
