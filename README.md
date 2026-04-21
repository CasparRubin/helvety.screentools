# Helvety Screen Tools

WinUI 3 desktop app for Windows with **screenshot** (frozen-screen selection) and **Live Draw** (full-screen overlay: a **`WS_EX_NOREDIRECTIONBITMAP`** Win32 host with **`DwmExtendFrameIntoClientArea`** and a **transparent** WinUI XAML island so the real desktop—including video—shows through with zero color shift). Ink is **WinUI XAML**, not ZoomIt’s off-screen GDI buffer. Live Draw does **not** use a GDI BitBlt **snapshot** of the desktop or a refresh loop.

## Development Status

The app can be **packaged and deployed** (MSIX or unpackaged). Behavior and defaults may still **change between releases**; see **Notes** below when upgrading or supporting users.

## Current Focus

- **Two independent global hotkeys**: screenshot (default `Shift+S+S+S`) and Live Draw (default `Shift+D+D+D`). They must use different sequences; the app blocks applying duplicates in Settings.
- **Settings layout**: the main **NavigationView** has a single left rail. **Settings** is an expandable group (PowerToys-style) with **General**, **Screenshot**, and **Live Draw**. **General** covers close-to-tray, **Run at sign-in** (Windows **Startup** apps; packaged MSIX only—hidden when running unpackaged), editor performance, global **snap-border intensity** (Subtle / Balanced / Bold; **default Bold**) for both screenshot and Live Draw, and a **Reset all settings** action (does not change Windows startup registration). **Screenshot** includes save folder, master **On/Off**, hotkey editor, screenshot quality, and overlay instructions. **Live Draw** includes a master **On/Off** toggle (shortcuts stay stored when off), global hotkey editor, **Line thickness**, and **Shape shortcuts** (per-tool modifiers plus fixed free draw and sparkle).
- **Screenshot** (global hotkey): overlay with window snapping (highlighted border). **Esc** cancels and closes the overlay. **Click** (without dragging) commits the snapped window under the cursor, or the current monitor under the cursor if nothing snaps; **drag** selects a custom rectangle. Left-click saves, copies to clipboard, and exits; right-click saves (no clipboard copy) and stays in the same screenshot session.
- **Live Draw**: global hotkey → fullscreen **Win32 `WS_EX_NOREDIRECTIONBITMAP` host** with a **DesktopWindowXamlSource** island (vector markup only; no desktop BitBlt). **Esc** ends the session. **Settings → Live Draw → Line thickness** controls the stroke width for rectangles/ellipses/freehand arrows and straight-line ink. **Settings → Live Draw → Shape shortcuts** assigns **Shift**, **Ctrl**, **Alt**, **Win**, or **None** (unbound) to **Rectangle**, **Arrow**, and **Straight line** (left mouse; each non-None modifier must be unique) and to **Circle** and **Ellipse** (right drag; must differ unless one is **None**). Left drag with **no** matching shape modifier is **free draw**; right click and hold with **no** circle/ellipse modifier is the **sparkle** animation (fixed). **Rectangles, ellipses, circles, arrows, straight lines, freehand, and sparkle** use the same snap-border chrome as screenshot selection (gradients, dashing, pulse). **Does not require a save folder**; saving screenshots from screenshot mode still requires a writable save location.
- **Navigation**: left **NavigationView** with **General** (home), an expandable **Settings** group (**General**, **Screenshot**, **Live Draw**), and **About**. **About** shows a short product summary, the app **Version** (package version when installed; assembly version when running unpackaged), and links to [helvety.com](https://helvety.com/) and the [GitHub repository](https://github.com/CasparRubin/helvety.screentools).
- Close-to-tray behavior (notification area) with optional full-exit-on-close setting.
- Restore main window after screenshot or Live Draw when the window was tray-hidden (screenshot path restores after at least one save; Live Draw restores after the session ends).
- **General** home page (titled **Helvety Screen Tools**): after each **screenshot** is saved to the configured folder, the coordinator notifies this page so the gallery **reloads** (same as navigating back to home). Other saves (for example from the editor) rely on the folder watcher and debounced rescan like any file change. For **`.png`** files thumbnails use a **scaled decode from disk** (about max width 520px), then a generic file decode, then the **shell thumbnail** provider. A **debounced** `FileSystemWatcher` still runs a full folder rescan so ordering and metadata stay correct. **Listing files** for a refresh runs on a **background thread** before the gallery lock; applying updates to the grid stays **serialized**. Images are grouped into date sections (**Today**, **Yesterday**, **Last Week**, **Older**). Thumbnails attach to the **visible row by file path**. In-flight thumbnail work is **not** cancelled on every refresh (only when leaving the page). The images header sits above the card and reminds you that **left-click** opens the image in the editor and **right-click** copies the image to the clipboard.
- Thumbnail previews for common image formats (PNG, JPG/JPEG, BMP, GIF, TIFF; WebP when a codec is installed)
- File metadata shown as European date/time format (`dd.MM.yyyy HH:mm`) plus relative age (`... ago`)
- Built-in image editor tools (Move, Text, Border, Blur, Highlight, Arrow, Crop; Crop is last in the toolbar)
- Single-row editor settings strip with horizontal overflow handling; settings switch contextually to the active tool (or selected layer while Move is active)
- Layer list with drag-and-drop reordering (top item is rendered in front)
- Export section under Layers (bottom-right) with **Save, copy and close** and **Save and close** (or **Save crop...** variants while crop is active); each save writes a new flattened PNG and closes the editor, while the first option also copies the result to clipboard
- Saved edits are intentionally non-reeditable: reopening a saved image starts from the bitmap result, and new annotations can be added in a fresh edit session
- Arrow drawing with live preview while dragging
- Quick text re-edit via selected-layer editor controls
- Blur settings include Radius, Corner Radius, Feather, and Invert; Highlight includes Dim, Corner Radius, and Invert (inside/outside targeting)
- GPU-accelerated blur/highlight effects (Win2D-based) with interaction-first recomposition scheduling
- Editor performance optimizations: coalesced recomposition, reduced overlay churn, and deferred pixel-effect recomposition during drag/resize
- Optional **Editor performance mode** toggle under **Settings → General** to prioritize responsiveness during intensive layer editing
- Iterative UX polish (overlay guidance, animation, interaction tuning)
- **Snap-border overlay** (screenshot + Live Draw): shared animated chrome (gradients, dashing, pulse). Global **intensity** (Subtle / Balanced / Bold) is under **Settings → General**. The **image editor** uses separate stroke and effect controls for layers.
- Selection screenshot quality modes (`Fast`, `Optimized`, `Heavy`) to trade off speed vs text readability enhancement; **default is `Fast`** for responsive saves. **`Heavy`** applies large upscales and filters and can take several seconds on big selections—use it when you explicitly want maximum enhancement.
- Settings-controlled overlay guidance visibility for **screenshot** mode
- Mutual exclusion between screenshot overlay and Live Draw via a shared gate (only one session at a time)

## Tech Stack

- .NET 8
- WinUI 3 (Windows App SDK)
- Native Win32 interop (global hotkeys, window hit-testing, GDI freeze-frame for selection overlay, `WS_EX_NOREDIRECTIONBITMAP` host for Live Draw)
- Self-contained Windows App SDK runtime (unpackaged `dotnet run` / F5 without a separate machine-wide Windows App Runtime install)
- `H.NotifyIcon.WinUI` for notification area tray integration

## Install

### Download from GitHub Releases (recommended)

Go to the [**Releases**](https://github.com/CasparRubin/helvety.screentools/releases) page, download the ZIP for your platform (`win-x64` or `win-arm64`), extract it anywhere, and run **`helvety.screentools.exe`**. The app is self-contained — no .NET or Windows App Runtime install required.

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
dotnet msbuild helvety.screentools/helvety.screentools.csproj -p:Configuration=Release -p:Platform=x64 -t:Publish -p:GenerateAppxPackageOnBuild=true
```

The **`.msix`** file is written under **`helvety.screentools/AppPackages/`**, inside a subfolder named for the app, version, and platform (open that folder and use the `.msix` you find there).

#### Install the MSIX

- **Double-click** the `.msix` and confirm the installer, or from PowerShell:  
  `Add-AppxPackage -Path "<path-to-your>.msix"`

**Signing and trust:** packages produced on a dev machine are often **unsigned** or signed with a **self-signed** certificate. Windows may refuse to install until you either:

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
   - **General**: **System tray** (close-to-tray), **Run at sign-in** (when packaged), **Editor performance mode**, and **Snap border** intensity (global for screenshot selection and Live Draw).
   - **Screenshot**: **Save location** (required for screenshot), master toggle, **Listen**-based hotkey editor (changes auto-save), **Screenshot quality**, and **Overlay** instructions.
   - **Live Draw**: master toggle, **Listen**-based hotkey editor (changes auto-save), **Line thickness**, and **Shape shortcuts** (per-tool modifiers). **Live Draw** can be configured without a save folder.
   - **Settings → General** also includes a **Reset all settings** action: all stored preferences revert to defaults (including the save folder, which is set again to the default desktop **Helvety Screen Tools captures** folder). Existing image files on disk are **not** deleted.
5. (Optional) Under **General → System tray**, choose whether closing the window keeps the app in the notification area (default) or fully exits.

## Releasing

A [GitHub Actions workflow](.github/workflows/release.yml) automates builds and GitHub Releases. To publish a new version:

1. Bump the version in `helvety.screentools.csproj` (`ApplicationDisplayVersion`, `ApplicationVersion`, and `Version`), `Package.appxmanifest`, and `app.manifest` so they stay aligned.
2. Commit the version bump.
3. Tag and push:

```bash
git tag v1.4.0
git push origin v1.4.0
```

The workflow builds self-contained portable ZIPs for **x64** and **arm64**, then creates a GitHub Release with auto-generated release notes and the ZIPs attached. No certificate or MSIX signing is needed for the portable distribution.

## Notes

- **Automated tests**: the solution currently has **no** `dotnet test` project; validation is by **build** (`dotnet build helvety.screentools.slnx` or the `.csproj` with `-c Release` and the desired `-p:Platform=`).
- **About → Version**: **installed MSIX** shows `Package.Current` identity (matches release versioning). **Unpackaged** runs show the assembly version from the same `Version` MSBuild property.
- **Screenshot quality** defaults to **Fast** for new settings profiles (missing `ScreenshotQualityMode` in LocalSettings). If you previously saved **Heavy** or **Optimized**, that choice is kept until you change it under **Settings → Screenshot → Screenshot quality**.
- Default save folder when the app first creates one (and you have not cleared it in Settings) is **`Helvety Screen Tools captures`** on your desktop. The top-nav home item is labeled **General** and the page title is **Helvety Screen Tools**; this is not tied to any legacy “Screenshots” folder name.
- Quality enhancement modes improve perceived readability for some text-heavy screenshots, but they cannot guarantee recovery of detail that is not present in source screen pixels.
