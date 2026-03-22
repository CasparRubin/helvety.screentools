using Microsoft.UI.Xaml.Controls;

namespace helvety.screentools.Views
{
    /// <summary>
    /// About page: product summary, compile-time build stamp in <c>yyMMdd_HHmmss</c> form (<see cref="AppBuildInfo"/>),
    /// and links to helvety.com and the public GitHub repository.
    /// </summary>
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            BuildStampText.Text = AppBuildInfo.BuildStamp;
        }
    }
}
