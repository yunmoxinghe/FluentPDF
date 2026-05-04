using System;
using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace FluentPDF.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded   += SettingsPage_Loaded;
            this.Unloaded += SettingsPage_Unloaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUI();
            LoadAppInfo();
            _isInitializing = false;

            if (Window.Current.Content is FrameworkElement root)
            {
                root.ActualThemeChanged -= AppThemeManager.OnActualThemeChanged;
                root.ActualThemeChanged += AppThemeManager.OnActualThemeChanged;
            }
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (Window.Current.Content is FrameworkElement root)
                root.ActualThemeChanged -= AppThemeManager.OnActualThemeChanged;
        }

        private void LoadUI()
        {
            var s = SettingsManager.Instance;
            RbTheme.SelectedIndex = s.AppTheme switch { "Light" => 1, "Dark" => 2, _ => 0 };
            RbMaterial.SelectedIndex = s.AppMaterial == "Acrylic" ? 1 : 0;
            SoundToggle.IsOn = s.EnableSound;
            ShowWelcomeToggle.IsOn = s.ShowWelcomeOnLaunch;
            AllowCloseWelcomeToggle.IsOn = s.AllowCloseWelcomeWhenFileOpen;
            SchoolModeToggle.IsOn = s.SchoolMode;
        }

        private void LoadAppInfo()
        {
            try
            {
                TxtAppName.Text = Package.Current.DisplayName;
                var v = Package.Current.Id.Version;
                TxtVersion.Text = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                TxtCopyright.Text = $"©{DateTime.Now.Year} {Package.Current.PublisherDisplayName}。保留所有权利。";
            }
            catch (Exception ex) { Debug.WriteLine($"LoadAppInfo 错误: {ex.Message}"); }
        }

        private void OpenExternalLink(object sender, RoutedEventArgs e)
            => MainPage.Instance?.OpenExternalLink(sender, e);

        private void RbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            string value = RbTheme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
            SettingsManager.Instance.AppTheme = value;
            AppThemeManager.CurrentTheme = RbTheme.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            if (Window.Current.Content is FrameworkElement root)
                root.RequestedTheme = AppThemeManager.CurrentTheme;
        }

        private void RbMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            string value = RbMaterial.SelectedIndex == 1 ? "Acrylic" : "Mica";
            SettingsManager.Instance.AppMaterial = value;
            AppThemeManager.CurrentMaterial = value == "Acrylic"
                ? BackgroundMaterial.Acrylic : BackgroundMaterial.Mica;
            AppThemeManager.ApplyMaterial();
        }

private void SoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SettingsManager.Instance.EnableSound = SoundToggle.IsOn;
            MainPage.Instance?.ApplySettings();
        }

        private void ShowWelcomeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SettingsManager.Instance.ShowWelcomeOnLaunch = ShowWelcomeToggle.IsOn;
        }

        private void AllowCloseWelcomeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SettingsManager.Instance.AllowCloseWelcomeWhenFileOpen = AllowCloseWelcomeToggle.IsOn;
        }

        private void SchoolModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SettingsManager.Instance.SchoolMode = SchoolModeToggle.IsOn;
            MainPage.Instance?.ApplySettings();
        }
    }
}
