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
    }
}
