using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace FluentPDF.Pages
{
    public sealed partial class WelcomePage : Page
    {
        public event EventHandler? OpenFileRequested;

        public WelcomePage()
        {
            this.InitializeComponent();
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
            => OpenFileRequested?.Invoke(this, EventArgs.Empty);

        private void Page_DoubleTapped(object sender, Windows.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
            => OpenFileRequested?.Invoke(this, EventArgs.Empty);

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
            => MainPage.Instance?.OpenSettingsTab();
    }
}
