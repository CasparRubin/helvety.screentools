using System;
using System.Collections.ObjectModel;

namespace helvety.screentools.Editor
{
    internal enum EditorToolType
    {
        /// <summary>Canvas tool: select, move, and resize layers. Settings strip shows viewport zoom (<c>Size</c> card) or the selected layer's tool when applicable.</summary>
        Canvas = 0,
        Text = 1,
        Border = 2,
        Blur = 3,
        Arrow = 4,
        Highlight = 5,
        Crop = 6,
        Magnifier = 7
    }

    internal enum EditorLayerType
    {
        Text = 0,
        Border = 1,
        Blur = 2,
        Arrow = 3,
        Highlight = 4,
        Magnifier = 5
    }

    internal enum MagnifierShape
    {
        Circle = 0,
        Rectangle = 1
    }

    internal enum MagnifierPart
    {
        None = 0,
        Source = 1,
        Loupe = 2
    }

    internal enum ArrowFormStyle
    {
        /// <summary>Default tapered shaft with filled head (image editor Arrow tool).</summary>
        Tapered = 0,
        /// <summary>Straight shaft with filled head (image editor Arrow tool).</summary>
        Straight = 1,
        /// <summary>Line without a head; used by Live Draw straight-line ink, not exposed in the image editor UI.</summary>
        LineOnly = 2
    }

    internal sealed class EditorDocument
    {
        internal EditorDocument(string sourcePath, int width, int height)
        {
            SourcePath = sourcePath;
            Width = width;
            Height = height;
            Layers = new ObservableCollection<EditorLayer>();
        }

        internal string SourcePath { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal ObservableCollection<EditorLayer> Layers { get; }
    }

    internal abstract class EditorLayer
    {
        protected EditorLayer(string name, EditorLayerType layerType)
        {
            Id = Guid.NewGuid();
            Name = name;
            LayerType = layerType;
            IsVisible = true;
        }

        internal Guid Id { get; }

        internal string Name { get; set; }

        internal EditorLayerType LayerType { get; }

        internal bool IsVisible { get; set; }

        internal abstract EditorRect GetBounds();

        internal abstract bool ContainsPoint(double x, double y);

        internal abstract void MoveBy(double deltaX, double deltaY, int maxWidth, int maxHeight);
    }

    internal sealed class TextLayer : EditorLayer
    {
        private string _text;
        private double _x;
        private double _y;
        private double _fontSize;
        private int _wrapWidth;
        private EditorRect? _cachedBounds;

        internal TextLayer(string text, double x, double y, double fontSize, string colorHex, int wrapWidth)
            : base($"Text: {TrimName(text)}", EditorLayerType.Text)
        {
            _text = text ?? string.Empty;
            _x = x;
            _y = y;
            _fontSize = fontSize;
            ColorHex = colorHex;
            _wrapWidth = Math.Max(1, wrapWidth);
        }

        internal string Text
        {
            get => _text;
            private set
            {
                _text = value ?? string.Empty;
                InvalidateBounds();
            }
        }

        internal double X
        {
            get => _x;
            set
            {
                _x = value;
                InvalidateBounds();
            }
        }

        internal double Y
        {
            get => _y;
            set
            {
                _y = value;
                InvalidateBounds();
            }
        }

        internal double FontSize
        {
            get => _fontSize;
            set
            {
                _fontSize = value;
                InvalidateBounds();
            }
        }

        internal string ColorHex { get; set; }

        internal string FontFamily { get; set; } = "Segoe UI";

        internal bool IsBold { get; set; } = true;

        internal bool IsItalic { get; set; }

        internal bool HasBorder { get; set; } = true;

        internal string BorderColorHex { get; set; } = "#FFFFFFFF";

        internal int BorderThickness { get; set; } = 1;

        internal bool HasShadow { get; set; } = true;

        internal int ShadowOffset { get; set; } = 2;

        internal string ShadowColorHex { get; set; } = "#66000000";

        internal int WrapWidth => _wrapWidth;

        internal override EditorRect GetBounds()
        {
            if (_cachedBounds.HasValue)
            {
                return _cachedBounds.Value;
            }

            var charWidth = Math.Max(1.0, FontSize * 0.62);
            var lineHeight = Math.Max(1.0, FontSize * 1.35);
            var safeWrapWidth = Math.Max(1, WrapWidth);
            var maxCharsPerVisualLine = Math.Max(1, (int)Math.Floor(safeWrapWidth / charWidth));

            var rawLines = (Text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var visualLineCount = 0;
            var maxLineCharCount = 1;

            foreach (var rawLine in rawLines)
            {
                var lineLength = Math.Max(0, rawLine.Length);
                if (lineLength == 0)
                {
                    visualLineCount++;
                    continue;
                }

                var wraps = (int)Math.Ceiling(lineLength / (double)maxCharsPerVisualLine);
                visualLineCount += Math.Max(1, wraps);
                maxLineCharCount = Math.Max(maxLineCharCount, Math.Min(lineLength, maxCharsPerVisualLine));
            }

            visualLineCount = Math.Max(1, visualLineCount);
            var measuredWidth = (int)Math.Ceiling(maxLineCharCount * charWidth);
            var width = Math.Max(1, Math.Min(safeWrapWidth, measuredWidth));
            var height = Math.Max(1, (int)Math.Ceiling(visualLineCount * lineHeight));
            _cachedBounds = new EditorRect((int)Math.Round(X), (int)Math.Round(Y), width, height);
            return _cachedBounds.Value;
        }

        internal override bool ContainsPoint(double x, double y)
        {
            var bounds = GetBounds();
            return x >= bounds.X &&
                   y >= bounds.Y &&
                   x <= bounds.X + bounds.Width &&
                   y <= bounds.Y + bounds.Height;
        }

        internal override void MoveBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            var bounds = GetBounds();
            var targetX = (int)Math.Round(X + deltaX);
            var targetY = (int)Math.Round(Y + deltaY);
            targetX = Math.Clamp(targetX, 0, Math.Max(0, maxWidth - bounds.Width));
            targetY = Math.Clamp(targetY, 0, Math.Max(0, maxHeight - bounds.Height));
            X = targetX;
            Y = targetY;
        }

        internal void UpdateText(string text)
        {
            Text = text ?? string.Empty;
            Name = $"Text: {TrimName(Text)}";
        }

        internal void UpdateWrapWidth(int width)
        {
            _wrapWidth = Math.Max(1, width);
            InvalidateBounds();
        }

        private void InvalidateBounds()
        {
            _cachedBounds = null;
        }

        private static string TrimName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Layer";
            }

            var value = text.Trim();
            return value.Length <= 18 ? value : $"{value[..18]}...";
        }
    }

    internal sealed class BorderLayer : EditorLayer
    {
        internal BorderLayer(EditorRect region, int thickness, string colorHex)
            : base($"Border ({region.Width}x{region.Height})", EditorLayerType.Border)
        {
            Region = region;
            Thickness = thickness;
            ColorHex = colorHex;
        }

        internal EditorRect Region { get; set; }

        internal int Thickness { get; set; }

        internal int CornerRadius { get; set; }

        internal string ColorHex { get; set; }

        internal bool HasShadow { get; set; } = true;

        internal string ShadowColorHex { get; set; } = "#66000000";

        internal int ShadowOffset { get; set; } = 2;

        internal override EditorRect GetBounds()
        {
            return Region;
        }

        internal override bool ContainsPoint(double x, double y)
        {
            var region = Region;
            return x >= region.X &&
                   y >= region.Y &&
                   x <= region.X + region.Width &&
                   y <= region.Y + region.Height;
        }

        internal override void MoveBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            var targetX = (int)Math.Round(Region.X + deltaX);
            var targetY = (int)Math.Round(Region.Y + deltaY);
            targetX = Math.Clamp(targetX, 0, Math.Max(0, maxWidth - Region.Width));
            targetY = Math.Clamp(targetY, 0, Math.Max(0, maxHeight - Region.Height));
            Region = Region with { X = targetX, Y = targetY };
        }
    }

    internal sealed class BlurLayer : EditorLayer
    {
        internal BlurLayer(EditorRect region, int radius)
            : base($"Blur ({region.Width}x{region.Height})", EditorLayerType.Blur)
        {
            Region = region;
            Radius = radius;
        }

        internal EditorRect Region { get; set; }

        internal int Radius { get; set; }

        internal int Feather { get; set; }

        internal int CornerRadius { get; set; }

        internal override EditorRect GetBounds()
        {
            return Region;
        }

        internal override bool ContainsPoint(double x, double y)
        {
            var region = Region;
            return x >= region.X &&
                   y >= region.Y &&
                   x <= region.X + region.Width &&
                   y <= region.Y + region.Height;
        }

        internal override void MoveBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            var targetX = (int)Math.Round(Region.X + deltaX);
            var targetY = (int)Math.Round(Region.Y + deltaY);
            targetX = Math.Clamp(targetX, 0, Math.Max(0, maxWidth - Region.Width));
            targetY = Math.Clamp(targetY, 0, Math.Max(0, maxHeight - Region.Height));
            Region = Region with { X = targetX, Y = targetY };
        }
    }

    internal sealed class HighlightLayer : EditorLayer
    {
        internal HighlightLayer(EditorRect region)
            : base($"Highlight ({region.Width}x{region.Height})", EditorLayerType.Highlight)
        {
            Region = region;
        }

        internal EditorRect Region { get; set; }

        internal int CornerRadius { get; set; }

        internal override EditorRect GetBounds()
        {
            return Region;
        }

        internal override bool ContainsPoint(double x, double y)
        {
            var region = Region;
            return x >= region.X &&
                   y >= region.Y &&
                   x <= region.X + region.Width &&
                   y <= region.Y + region.Height;
        }

        internal override void MoveBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            var targetX = (int)Math.Round(Region.X + deltaX);
            var targetY = (int)Math.Round(Region.Y + deltaY);
            targetX = Math.Clamp(targetX, 0, Math.Max(0, maxWidth - Region.Width));
            targetY = Math.Clamp(targetY, 0, Math.Max(0, maxHeight - Region.Height));
            Region = Region with { X = targetX, Y = targetY };
        }
    }

    internal sealed class ArrowLayer : EditorLayer
    {
        internal ArrowLayer(double startX, double startY, double endX, double endY, double thickness, string colorHex, ArrowFormStyle formStyle)
            : base("Arrow", EditorLayerType.Arrow)
        {
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
            Thickness = Math.Max(1, thickness);
            ColorHex = colorHex;
            FormStyle = formStyle;
        }

        internal double StartX { get; set; }

        internal double StartY { get; set; }

        internal double EndX { get; set; }

        internal double EndY { get; set; }

        internal double Thickness { get; set; }

        internal string ColorHex { get; set; }

        internal ArrowFormStyle FormStyle { get; set; }

        internal bool HasBorder { get; set; } = false;

        internal string BorderColorHex { get; set; } = "#FFFFFFFF";

        internal int BorderThickness { get; set; } = 1;

        internal bool HasShadow { get; set; } = true;

        internal string ShadowColorHex { get; set; } = "#66000000";

        internal int ShadowOffset { get; set; } = 2;

        internal override EditorRect GetBounds()
        {
            var minX = (int)Math.Floor(Math.Min(StartX, EndX));
            var minY = (int)Math.Floor(Math.Min(StartY, EndY));
            var maxX = (int)Math.Ceiling(Math.Max(StartX, EndX));
            var maxY = (int)Math.Ceiling(Math.Max(StartY, EndY));
            var padding = Math.Max(8, (int)Math.Ceiling(Thickness * 2.5));
            if (FormStyle == ArrowFormStyle.Tapered)
            {
                padding = Math.Max(padding, (int)Math.Ceiling(Thickness * 3.2));
            }
            return new EditorRect(
                Math.Max(0, minX - padding),
                Math.Max(0, minY - padding),
                Math.Max(1, (maxX - minX) + (padding * 2)),
                Math.Max(1, (maxY - minY) + (padding * 2)));
        }

        internal override bool ContainsPoint(double x, double y)
        {
            var bounds = GetBounds();
            if (x < bounds.X || y < bounds.Y || x > bounds.X + bounds.Width || y > bounds.Y + bounds.Height)
            {
                return false;
            }

            var distance = DistanceToSegment(x, y, StartX, StartY, EndX, EndY);
            var selectionRadius = Math.Max(6.0, Thickness + 3.0);
            if (FormStyle == ArrowFormStyle.Tapered)
            {
                selectionRadius = Math.Max(selectionRadius, (Thickness * 2.6) + 3.0);
            }

            return distance <= selectionRadius;
        }

        internal override void MoveBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            var bounds = GetBounds();
            var targetX = Math.Clamp((int)Math.Round(bounds.X + deltaX), 0, Math.Max(0, maxWidth - bounds.Width));
            var targetY = Math.Clamp((int)Math.Round(bounds.Y + deltaY), 0, Math.Max(0, maxHeight - bounds.Height));
            var moveX = targetX - bounds.X;
            var moveY = targetY - bounds.Y;
            StartX += moveX;
            StartY += moveY;
            EndX += moveX;
            EndY += moveY;
        }

        private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001)
            {
                return Math.Sqrt(((px - x1) * (px - x1)) + ((py - y1) * (py - y1)));
            }

            var t = (((px - x1) * dx) + ((py - y1) * dy)) / ((dx * dx) + (dy * dy));
            t = Math.Clamp(t, 0, 1);
            var projX = x1 + (t * dx);
            var projY = y1 + (t * dy);
            var diffX = px - projX;
            var diffY = py - projY;
            return Math.Sqrt((diffX * diffX) + (diffY * diffY));
        }

    }

    internal sealed class MagnifierLayer : EditorLayer
    {
        internal const double MinZoom = 1.5;
        internal const double MaxZoom = 4.0;
        internal const int MinRegionSize = 8;

        private double _zoom;

        internal MagnifierLayer(
            EditorRect sourceRegion,
            EditorRect loupeRegion,
            MagnifierShape shape,
            double zoom,
            int thickness,
            string colorHex)
            : base($"Magnifier ({sourceRegion.Width}x{sourceRegion.Height})", EditorLayerType.Magnifier)
        {
            SourceRegion = sourceRegion;
            LoupeRegion = loupeRegion;
            Shape = shape;
            _zoom = ClampZoom(zoom);
            Thickness = Math.Max(1, thickness);
            ColorHex = colorHex;
        }

        internal EditorRect SourceRegion { get; private set; }

        internal EditorRect LoupeRegion { get; private set; }

        internal MagnifierShape Shape { get; set; }

        internal double Zoom
        {
            get => _zoom;
            private set => _zoom = ClampZoom(value);
        }

        internal int Thickness { get; set; }

        internal string ColorHex { get; set; }

        internal bool HasShadow { get; set; } = true;

        internal string ShadowColorHex { get; set; } = "#66000000";

        internal int ShadowOffset { get; set; } = 2;

        /// <summary>Bumped when loupe pixel content is rebuilt so vector sync invalidates the cached image.</summary>
        internal int ContentGeneration { get; set; }

        /// <summary>Nearest-neighbor BGRA loupe buffer (transient; rebuilt during compose).</summary>
        internal byte[]? LoupePixels { get; set; }

        internal int LoupePixelWidth { get; set; }

        internal int LoupePixelHeight { get; set; }

        internal override EditorRect GetBounds()
        {
            return Union(SourceRegion, LoupeRegion);
        }

        internal override bool ContainsPoint(double x, double y)
        {
            return HitTestPart(x, y) != MagnifierPart.None;
        }

        internal MagnifierPart HitTestPart(double x, double y)
        {
            if (RegionContainsPoint(LoupeRegion, x, y))
            {
                return MagnifierPart.Loupe;
            }

            if (RegionContainsPoint(SourceRegion, x, y))
            {
                return MagnifierPart.Source;
            }

            return MagnifierPart.None;
        }

        internal override void MoveBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            MoveSourceBy(deltaX, deltaY, maxWidth, maxHeight);
            MoveLoupeBy(deltaX, deltaY, maxWidth, maxHeight);
        }

        internal void MoveSourceBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            SourceRegion = ClampMove(SourceRegion, deltaX, deltaY, maxWidth, maxHeight);
            RefreshName();
        }

        internal void MoveLoupeBy(double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            LoupeRegion = ClampMove(LoupeRegion, deltaX, deltaY, maxWidth, maxHeight);
        }

        internal void RestoreRegions(EditorRect source, EditorRect loupe)
        {
            SourceRegion = source;
            LoupeRegion = loupe;
            RefreshName();
        }

        internal void SetSourceRegionKeepingZoom(EditorRect source, int maxWidth, int maxHeight)
        {
            var normalized = NormalizeSource(source);
            SourceRegion = ClampRectToImage(normalized, maxWidth, maxHeight);
            LoupeRegion = RebuildLoupeFromSource(SourceRegion, LoupeRegion, Zoom, maxWidth, maxHeight);
            RefreshName();
        }

        internal void SetLoupeRegionKeepingZoom(EditorRect loupe, int maxWidth, int maxHeight)
        {
            var normalizedLoupe = NormalizeLoupe(loupe);
            LoupeRegion = ClampRectToImage(normalizedLoupe, maxWidth, maxHeight);
            var sourceWidth = Math.Max(MinRegionSize, (int)Math.Round(LoupeRegion.Width / Zoom));
            var sourceHeight = Math.Max(MinRegionSize, (int)Math.Round(LoupeRegion.Height / Zoom));
            if (Shape == MagnifierShape.Circle)
            {
                var side = Math.Max(MinRegionSize, Math.Min(sourceWidth, sourceHeight));
                sourceWidth = side;
                sourceHeight = side;
                var loupeSide = Math.Max(MinRegionSize, (int)Math.Round(side * Zoom));
                LoupeRegion = ClampRectToImage(
                    new EditorRect(LoupeRegion.X, LoupeRegion.Y, loupeSide, loupeSide),
                    maxWidth,
                    maxHeight);
            }

            SourceRegion = ClampRectToImage(
                new EditorRect(SourceRegion.X, SourceRegion.Y, sourceWidth, sourceHeight),
                maxWidth,
                maxHeight);
            RefreshName();
        }

        internal void ApplyZoomKeepingSource(double zoom, int maxWidth, int maxHeight)
        {
            Zoom = zoom;
            LoupeRegion = RebuildLoupeFromSource(SourceRegion, LoupeRegion, Zoom, maxWidth, maxHeight);
            RefreshName();
        }

        internal static double ClampZoom(double zoom) => Math.Clamp(zoom, MinZoom, MaxZoom);

        internal static EditorRect CreateLoupeRegionFromSource(
            EditorRect source,
            double zoom,
            int maxWidth,
            int maxHeight)
        {
            var safeZoom = ClampZoom(zoom);
            var loupeWidth = Math.Max(MinRegionSize, (int)Math.Round(source.Width * safeZoom));
            var loupeHeight = Math.Max(MinRegionSize, (int)Math.Round(source.Height * safeZoom));
            var offsetX = (int)Math.Round(source.Width * 0.35);
            var offsetY = (int)Math.Round(source.Height * 0.35);
            var loupeX = source.X + source.Width + offsetX;
            var loupeY = source.Y + offsetY;
            if (loupeX + loupeWidth > maxWidth)
            {
                loupeX = Math.Max(0, source.X - loupeWidth - offsetX);
            }

            if (loupeY + loupeHeight > maxHeight)
            {
                loupeY = Math.Max(0, source.Y - loupeHeight - offsetY);
            }

            return ClampRectToImage(new EditorRect(loupeX, loupeY, loupeWidth, loupeHeight), maxWidth, maxHeight);
        }

        internal static EditorRect NormalizeCircleRegion(EditorRect region)
        {
            var side = Math.Max(MinRegionSize, Math.Min(region.Width, region.Height));
            return new EditorRect(region.X, region.Y, side, side);
        }

        private EditorRect NormalizeSource(EditorRect source)
        {
            var width = Math.Max(MinRegionSize, source.Width);
            var height = Math.Max(MinRegionSize, source.Height);
            if (Shape == MagnifierShape.Circle)
            {
                var side = Math.Max(MinRegionSize, Math.Min(width, height));
                return new EditorRect(source.X, source.Y, side, side);
            }

            return new EditorRect(source.X, source.Y, width, height);
        }

        private EditorRect NormalizeLoupe(EditorRect loupe)
        {
            var width = Math.Max(MinRegionSize, loupe.Width);
            var height = Math.Max(MinRegionSize, loupe.Height);
            if (Shape == MagnifierShape.Circle)
            {
                var side = Math.Max(MinRegionSize, Math.Min(width, height));
                return new EditorRect(loupe.X, loupe.Y, side, side);
            }

            return new EditorRect(loupe.X, loupe.Y, width, height);
        }

        private static EditorRect RebuildLoupeFromSource(
            EditorRect source,
            EditorRect currentLoupe,
            double zoom,
            int maxWidth,
            int maxHeight)
        {
            var loupeWidth = Math.Max(MinRegionSize, (int)Math.Round(source.Width * zoom));
            var loupeHeight = Math.Max(MinRegionSize, (int)Math.Round(source.Height * zoom));
            return ClampRectToImage(
                new EditorRect(currentLoupe.X, currentLoupe.Y, loupeWidth, loupeHeight),
                maxWidth,
                maxHeight);
        }

        private bool RegionContainsPoint(EditorRect region, double x, double y)
        {
            if (x < region.X || y < region.Y || x > region.X + region.Width || y > region.Y + region.Height)
            {
                return false;
            }

            if (Shape != MagnifierShape.Circle)
            {
                return true;
            }

            var radiusX = region.Width / 2.0;
            var radiusY = region.Height / 2.0;
            if (radiusX <= 0 || radiusY <= 0)
            {
                return false;
            }

            var cx = region.X + radiusX;
            var cy = region.Y + radiusY;
            var nx = (x - cx) / radiusX;
            var ny = (y - cy) / radiusY;
            return (nx * nx) + (ny * ny) <= 1.0;
        }

        private static EditorRect ClampMove(EditorRect region, double deltaX, double deltaY, int maxWidth, int maxHeight)
        {
            var targetX = (int)Math.Round(region.X + deltaX);
            var targetY = (int)Math.Round(region.Y + deltaY);
            targetX = Math.Clamp(targetX, 0, Math.Max(0, maxWidth - region.Width));
            targetY = Math.Clamp(targetY, 0, Math.Max(0, maxHeight - region.Height));
            return region with { X = targetX, Y = targetY };
        }

        private static EditorRect ClampRectToImage(EditorRect region, int maxWidth, int maxHeight)
        {
            var width = Math.Clamp(region.Width, MinRegionSize, Math.Max(MinRegionSize, maxWidth));
            var height = Math.Clamp(region.Height, MinRegionSize, Math.Max(MinRegionSize, maxHeight));
            var x = Math.Clamp(region.X, 0, Math.Max(0, maxWidth - width));
            var y = Math.Clamp(region.Y, 0, Math.Max(0, maxHeight - height));
            return new EditorRect(x, y, width, height);
        }

        private static EditorRect Union(EditorRect a, EditorRect b)
        {
            var left = Math.Min(a.X, b.X);
            var top = Math.Min(a.Y, b.Y);
            var right = Math.Max(a.X + a.Width, b.X + b.Width);
            var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new EditorRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        private void RefreshName()
        {
            Name = $"Magnifier ({SourceRegion.Width}x{SourceRegion.Height})";
        }
    }

    internal readonly record struct EditorRect(int X, int Y, int Width, int Height)
    {
        internal bool IsEmpty => Width <= 0 || Height <= 0;
    }
}
