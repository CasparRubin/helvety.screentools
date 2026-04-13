using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;

namespace helvety.screentools
{
    internal sealed record AppSettings(
        string? SaveFolderPath,
        HotkeySettings? Hotkey,
        bool IsHotkeyCleared,
        bool IsSaveFolderCleared,
        /// <summary>Global snap-border intensity for capture selection overlay and Live Draw.</summary>
        ScreenshotBorderIntensity ScreenshotBorderIntensity,
        ScreenshotQualityMode ScreenshotQualityMode,
        bool ShowScreenshotOverlayInstructions,
        bool MinimizeToTrayOnClose,
        /// <summary>User wants the packaged app registered for sign-in startup (see <see cref="Services.StartupLaunchService"/>).</summary>
        bool RunAtWindowsStartup,
        /// <summary>When false, the low-level keyboard hook ignores capture and Live Draw chord sequences (Escape-to-cancel during overlays may still run).</summary>
        bool GlobalHotkeyListenersEnabled,
        bool CaptureHotkeyEnabled,
        bool LiveDrawEnabled);

    internal sealed record HotkeySettings(uint Modifiers, IReadOnlyList<uint> Sequence, string Display);

    internal sealed record EditorUiSettings(
        string PrimaryColorHex,
        int PrimaryThickness,
        string TextFont,
        int TextSize,
        bool TextBoldEnabled,
        bool TextItalicEnabled,
        bool TextBorderEnabled,
        string TextBorderColorHex,
        int TextBorderThickness,
        bool TextShadowEnabled,
        bool BorderShadowEnabled,
        bool ArrowBorderEnabled,
        string ArrowBorderColorHex,
        int ArrowBorderThickness,
        bool ArrowShadowEnabled,
        string ArrowFormStyle,
        int BlurRadius,
        int BlurFeather,
        bool BlurInvertMode,
        int HighlightDimPercent,
        bool HighlightInvertMode,
        int RegionCornerRadius,
        bool PerformanceModeEnabled,
        bool GpuEffectsEnabled);

    /// <summary>
    /// Visual strength of shared snap-border chrome (gradients, dashing, pulse) for frozen-screen capture and Live Draw.
    /// </summary>
    internal enum ScreenshotBorderIntensity
    {
        Subtle = 0,
        Balanced = 1,
        Bold = 2
    }

    internal enum ScreenshotQualityMode
    {
        Fast = 0,
        Optimized = 1,
        Heavy = 2
    }

    internal readonly record struct LiveDrawShapeModifiers(
        LiveDrawRectangleModifier Rectangle,
        LiveDrawRectangleModifier Arrow,
        LiveDrawRectangleModifier StraightLine,
        LiveDrawRectangleModifier CircleRight,
        LiveDrawRectangleModifier EllipseRight);

    internal sealed record LiveDrawDrawingSettings(
        int MainStrokeThickness,
        bool FreeDrawEnabled,
        bool SparkleEnabled);

    internal enum LiveDrawRectangleModifier
    {
        Shift = 0,
        Control = 1,
        Win = 2,
        Alt = 3,
        None = 4
    }

    internal sealed record GlobalSetupIssue(
        InfoBarSeverity Severity,
        string Title,
        string Message,
        string PrimaryActionText,
        string PrimaryActionTag,
        string SecondaryActionText,
        string SecondaryActionTag);
}
