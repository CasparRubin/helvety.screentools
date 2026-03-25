using System;
using System.Collections.ObjectModel;

namespace helvety.screentools.Editor
{
    internal enum EditorToolType
    {
        Move = 0,
        Text = 1,
        Border = 2,
        Blur = 3,
        Arrow = 4,
        Highlight = 5,
        Crop = 6
    }

    internal enum EditorLayerType
    {
        Text = 0,
        Border = 1,
        Blur = 2,
        Arrow = 3,
        Highlight = 4
    }

    internal enum ArrowFormStyle
    {
        Tapered = 0,
        Straight = 1,
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

        internal bool HasBorder { get; set; } = true;

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

    internal readonly record struct EditorRect(int X, int Y, int Width, int Height)
    {
        internal bool IsEmpty => Width <= 0 || Height <= 0;
    }
}
