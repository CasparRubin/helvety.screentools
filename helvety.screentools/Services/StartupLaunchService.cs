using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace helvety.screentools.Services
{
    internal static class StartupLaunchService
    {
        internal const string StartupTaskId = "HelvetyScreenToolsStartup";

        internal static bool IsSupported
        {
            get
            {
                try
                {
                    _ = Package.Current.Id;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static async Task<StartupTaskState?> GetStateAsync()
        {
            if (!IsSupported)
            {
                return null;
            }

            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                return task.State;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Asks Windows to enable startup. Returns the resulting <see cref="StartupTaskState"/> or null on failure.
        /// </summary>
        internal static async Task<StartupTaskState?> RequestEnableAsync()
        {
            if (!IsSupported)
            {
                return null;
            }

            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                return await task.RequestEnableAsync();
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static async Task DisableAsync()
        {
            if (!IsSupported)
            {
                return;
            }

            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                task.Disable();
            }
            catch (Exception)
            {
            }
        }
    }
}
