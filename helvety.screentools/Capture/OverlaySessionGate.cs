using System.Threading;

namespace helvety.screentools.Capture
{
    internal static class OverlaySessionGate
    {
        internal static readonly SemaphoreSlim Gate = new(1, 1);
    }
}
