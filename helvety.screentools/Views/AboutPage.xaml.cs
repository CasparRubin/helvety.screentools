using Microsoft.UI.Xaml.Controls;
using System.Reflection;
using Windows.ApplicationModel;

namespace helvety.screentools.Views
{
    /// <summary>
    /// About page: product summary, app version, and links.
    /// </summary>
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            AppVersionText.Text = FormatAppVersion();
        }

        private static string FormatAppVersion()
        {
            try
            {
                var v = Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "—";
            }
        }
    }
}
