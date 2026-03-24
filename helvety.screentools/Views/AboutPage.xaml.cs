using Microsoft.UI.Xaml.Controls;

namespace helvety.screentools.Views
{
    /// <summary>
    /// About page: product summary, compile-time build version (<see cref="AppBuildInfo"/>), and links.
    /// </summary>
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            BuildVersionText.Text = AppBuildInfo.BuildVersion;
        }
    }
}
