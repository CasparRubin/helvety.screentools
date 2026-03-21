using System;

namespace helvety.screentools.Capture
{
    /// <summary>Build 19041+ (Win10 2004): <c>WDA_EXCLUDEFROMCAPTURE</c> is reliable for GDI capture exclusion.</summary>
    internal static class LiveDrawPlatformSupport
    {
        private const int Windows10Version2004Build = 19041;

        internal static bool IsLiveDesktopRefreshSupported =>
            Environment.OSVersion.Version.Build >= Windows10Version2004Build;
    }
}
