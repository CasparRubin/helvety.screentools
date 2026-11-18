using System.Runtime.InteropServices;

namespace helvety.screentools
{
    /// <summary>
    /// Detects whether the process runs with MSIX package identity (installed MSIX / Visual Studio deploy)
    /// or unpackaged (release ZIP, <c>dotnet run</c>). Identity-only WinRT APIs must be gated on this.
    /// </summary>
    internal static class AppIdentity
    {
        private const int AppModelErrorNoPackage = 15700;

        internal static bool IsPackaged { get; } = DetectPackageIdentity();

        private static bool DetectPackageIdentity()
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
    }
}
