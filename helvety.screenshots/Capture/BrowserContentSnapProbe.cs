using Windows.Graphics;

namespace helvety.screenshots.Capture
{
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
