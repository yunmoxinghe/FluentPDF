using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace FluentPDF
{
    public sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;

                AppThemeManager.LoadSettings();
                AppThemeManager.ApplyTheme();
                AppThemeManager.ApplyMaterial();
            }

            if (rootFrame.Content == null)
                rootFrame.Navigate(typeof(MainPage));

            Window.Current.Activate();
        }

        protected override void OnFileActivated(FileActivatedEventArgs args)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;

                AppThemeManager.LoadSettings();
                AppThemeManager.ApplyTheme();
                AppThemeManager.ApplyMaterial();
            }

            if (rootFrame.Content == null)
                rootFrame.Navigate(typeof(MainPage));

            Window.Current.Activate();

            // 把文件传给已就绪的 MainPage
            if (rootFrame.Content is MainPage mainPage && args.Files.Count > 0)
                mainPage.OpenFile(args.Files[0] as StorageFile);
        }
    }

    // ── 主题管理器 ─────────────────────────────────────────────────
    public static class AppThemeManager
    {
        public static ElementTheme CurrentTheme = ElementTheme.Default;
        public static BackgroundMaterial CurrentMaterial = BackgroundMaterial.Mica;

        public static void LoadSettings()
        {
            var s = SettingsManager.Instance;

            try
            {
                CurrentTheme = s.AppTheme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
            catch { CurrentTheme = ElementTheme.Default; }

            try
            {
                CurrentMaterial = s.AppMaterial == "Acrylic"
                    ? BackgroundMaterial.Acrylic
                    : BackgroundMaterial.Mica;
            }
            catch { CurrentMaterial = BackgroundMaterial.Mica; }

            try
            {
                ElementSoundPlayer.State = s.EnableSound
                    ? ElementSoundPlayerState.On
                    : ElementSoundPlayerState.Off;
            }
            catch { ElementSoundPlayer.State = ElementSoundPlayerState.On; }
        }

        public static void ApplyTheme()
        {
            try
            {
                if (Window.Current.Content is FrameworkElement rootElement)
                    rootElement.RequestedTheme = CurrentTheme;

                CustomizeTitleBar();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyTheme failed: {ex.Message}");
            }
        }

        public static void ApplyMaterial()
        {
            try
            {
                var rootFrame = Window.Current.Content as Frame;
                if (rootFrame == null) return;

                if (CurrentMaterial == BackgroundMaterial.Mica)
                {
                    if (rootFrame is FrameworkElement el)
                        el.ActualThemeChanged -= OnActualThemeChanged;

                    rootFrame.Background = null;
                    Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(rootFrame, true);
                }
                else
                {
                    Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(rootFrame, false);

                    var isDark = GetIsDarkTheme();
                    var tintColor = isDark
                        ? Color.FromArgb(255, 32, 32, 32)
                        : Color.FromArgb(255, 243, 243, 243);

                    rootFrame.Background = new AcrylicBrush
                    {
                        BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                        TintColor = tintColor,
                        TintOpacity = 0.8,
                        FallbackColor = tintColor
                    };

                    if (rootFrame is FrameworkElement el)
                    {
                        el.ActualThemeChanged -= OnActualThemeChanged;
                        el.ActualThemeChanged += OnActualThemeChanged;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyMaterial failed: {ex.Message}");
            }
        }

        public static void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            CustomizeTitleBar();

            if (CurrentMaterial == BackgroundMaterial.Acrylic)
            {
                var rootFrame = Window.Current.Content as Frame;
                if (rootFrame == null) return;

                var isDark = GetIsDarkTheme();
                var tintColor = isDark
                    ? Color.FromArgb(255, 32, 32, 32)
                    : Color.FromArgb(255, 243, 243, 243);

                if (rootFrame.Background is AcrylicBrush brush)
                {
                    brush.TintColor = tintColor;
                    brush.FallbackColor = tintColor;
                }
            }
        }

        public static bool GetIsDarkTheme()
        {
            if (Window.Current?.Content is FrameworkElement rootElement)
            {
                var actual = rootElement.ActualTheme;
                if (actual != ElementTheme.Default)
                    return actual == ElementTheme.Dark;
            }
            if (CurrentTheme == ElementTheme.Default)
                return Application.Current.RequestedTheme == ApplicationTheme.Dark;
            return CurrentTheme == ElementTheme.Dark;
        }

        public static void CustomizeTitleBar()
        {
            try
            {
                var coreTitleBar = Windows.ApplicationModel.Core.CoreApplication.GetCurrentView().TitleBar;
                coreTitleBar.ExtendViewIntoTitleBar = true;

                var titleBar = ApplicationView.GetForCurrentView().TitleBar;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

                var isDark = GetIsDarkTheme();
                var fg = isDark ? Colors.White : Colors.Black;
                var inactiveFg = isDark
                    ? Color.FromArgb(255, 128, 128, 128)
                    : Color.FromArgb(255, 160, 160, 160);
                var hoverBg = isDark
                    ? Color.FromArgb(20, 255, 255, 255)
                    : Color.FromArgb(20, 0, 0, 0);

                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonInactiveForegroundColor = inactiveFg;
                titleBar.ButtonHoverBackgroundColor = hoverBg;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(30, hoverBg.R, hoverBg.G, hoverBg.B);
                titleBar.ButtonPressedForegroundColor = fg;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CustomizeTitleBar failed: {ex.Message}");
            }
        }
    }

    public enum BackgroundMaterial { Mica, Acrylic }

    [JsonSerializable(typeof(AppSettings))]
    internal sealed partial class AppSettingsJsonContext : JsonSerializerContext { }

    public sealed class SettingsManager
    {
        private static SettingsManager? _instance;
        public static SettingsManager Instance => _instance ??= new SettingsManager();

        private readonly string _settingsFilePath;
        private AppSettings _settings;

        private SettingsManager()
        {
            _settingsFilePath = Path.Combine(
                ApplicationData.Current.LocalFolder.Path, "app_settings.json");
            _settings = LoadSettingsFromFile();
        }

        public string AppTheme
        {
            get => _settings.AppTheme;
            set { _settings.AppTheme = value; SaveSettings(); }
        }
        public string AppMaterial
        {
            get => _settings.AppMaterial;
            set { _settings.AppMaterial = value; SaveSettings(); }
        }
        public string PanePosition
        {
            get => _settings.PanePosition;
            set { _settings.PanePosition = value; SaveSettings(); }
        }
        public bool EnableSound
        {
            get => _settings.EnableSound;
            set { _settings.EnableSound = value; SaveSettings(); }
        }

        private void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_settings, AppSettingsJsonContext.Default.AppSettings);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex) { Debug.WriteLine($"[SettingsManager] 保存失败: {ex.Message}"); }
        }

        private AppSettings LoadSettingsFromFile()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var s = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                    if (s != null) return s;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[SettingsManager] 加载失败: {ex.Message}"); }
            return new AppSettings();
        }
    }

    public sealed class AppSettings
    {
        public string AppTheme { get; set; } = "System";
        public string AppMaterial { get; set; } = "Mica";
        public string PanePosition { get; set; } = "Left";
        public bool EnableSound { get; set; } = true;
    }
}
