using System;

namespace helvety.screentools
{
    /// <summary>
    /// Lets content pages request shell navigation (e.g. open a Settings section) without holding a <see cref="MainWindow"/> reference.
    /// </summary>
    internal static class MainNavigationRequests
    {
        internal static event Action<string>? NavigateToTagRequested;

        internal static void RequestNavigateToTag(string tag)
        {
            NavigateToTagRequested?.Invoke(tag);
        }
    }
}
