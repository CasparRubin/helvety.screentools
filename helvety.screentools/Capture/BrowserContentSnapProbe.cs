using Windows.Graphics;

namespace helvety.screentools.Capture
{
    /// <summary>Pluggable probe for browser content bounds (e.g. future integration). Window snapping uses <see cref="NoopBrowserContentSnapProbe"/> until a real implementation exists.</summary>
    internal interface IBrowserContentSnapProbe
    {
        bool TryGetContentBoundsAt(int screenX, int screenY, out RectInt32 bounds);
    }

    internal sealed class NoopBrowserContentSnapProbe : IBrowserContentSnapProbe
    {
        public bool TryGetContentBoundsAt(int screenX, int screenY, out RectInt32 bounds)
        {
            bounds = default;
            return false;
        }
    }
}
