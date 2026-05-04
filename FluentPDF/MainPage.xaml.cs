using System;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using FluentPDF.Pages;
using TabView = Microsoft.UI.Xaml.Controls.TabView;
using TabViewItem = Microsoft.UI.Xaml.Controls.TabViewItem;
using TabViewTabCloseRequestedEventArgs = Microsoft.UI.Xaml.Controls.TabViewTabCloseRequestedEventArgs;

namespace FluentPDF
{
    public sealed partial class MainPage : Page
    {
        public static MainPage? Instance { get; private set; }

        public MainPage()
        {
            this.InitializeComponent();
            Instance = this;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            AppThemeManager.CustomizeTitleBar();

            if (Window.Current.Content is Windows.UI.Xaml.FrameworkElement rootEl)
                rootEl.ActualThemeChanged += (s, args) => AppThemeManager.CustomizeTitleBar();

            AddWelcomeTab();
        }

        private void CustomDragRegion_Loaded(object sender, RoutedEventArgs e)
        {
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;
            coreTitleBar.LayoutMetricsChanged += CoreTitleBar_LayoutMetricsChanged;

            UpdateTitleBarInsets(coreTitleBar);
            Window.Current.SetTitleBar(CustomDragRegion);

            System.Diagnostics.Debug.WriteLine(
                $"[TitleBar] CustomDragRegion actual H={CustomDragRegion.ActualHeight}, coreTitleBar.Height={coreTitleBar.Height}");
        }

        private void CoreTitleBar_LayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
            => UpdateTitleBarInsets(sender);

        private void UpdateTitleBarInsets(CoreApplicationViewTitleBar coreTitleBar)
        {
            // 不强制设置高度，让 CustomDragRegion / ShellTitlebarInset
            // 自然撑满 TabStripFooter / TabStripHeader 的实际高度（约48px）。
            // coreTitleBar.Height（32px）只用于计算 inset 宽度。
            ShellTitlebarInset.MinHeight = coreTitleBar.Height;
            CustomDragRegion.MinHeight   = coreTitleBar.Height;

            if (FlowDirection == FlowDirection.LeftToRight)
            {
                CustomDragRegion.MinWidth   = coreTitleBar.SystemOverlayRightInset;
                ShellTitlebarInset.MinWidth = coreTitleBar.SystemOverlayLeftInset;
            }
            else
            {
                CustomDragRegion.MinWidth   = coreTitleBar.SystemOverlayLeftInset;
                ShellTitlebarInset.MinWidth = coreTitleBar.SystemOverlayRightInset;
            }
        }

        // ── 从 App.OnFileActivated 调用 ───────────────────────────

        public void OpenFile(StorageFile? file)
        {
            if (file != null) OpenFileInNewTab(file);
        }

        // ── 设置标签 ──────────────────────────────────────────────

        public void OpenSettingsTab()
        {
            // 如果已有设置标签，直接切换到它
            foreach (var item in PdfTabView.TabItems)
            {
                if (item is TabViewItem existing && existing.Content is Pages.SettingsPage)
                {
                    PdfTabView.SelectedItem = existing;
                    return;
                }
            }

            var tab = new TabViewItem
            {
                Header = GetString("Settings_Breadcrumb"),
                IconSource = new Microsoft.UI.Xaml.Controls.SymbolIconSource { Symbol = Symbol.Setting },
                IsClosable = true,
                Content = new Pages.SettingsPage()
            };
            PdfTabView.TabItems.Add(tab);
            PdfTabView.SelectedItem = tab;
        }

        // ── 标签管理 ──────────────────────────────────────────────

        private void AddWelcomeTab()
        {
            var welcome = new WelcomePage();
            welcome.OpenFileRequested += async (s, e) => await PickAndOpenFile();

            var tab = new TabViewItem
            {
                Header = GetString("Tab_Welcome"),
                IconSource = new Microsoft.UI.Xaml.Controls.SymbolIconSource { Symbol = Symbol.Document },
                IsClosable = false,
                Content = welcome
            };
            PdfTabView.TabItems.Add(tab);
            PdfTabView.SelectedItem = tab;
        }

        public void OpenFileInNewTab(StorageFile file)
        {
            // 如果当前只有欢迎标签，直接替换
            if (PdfTabView.TabItems.Count == 1 &&
                PdfTabView.TabItems[0] is TabViewItem first &&
                first.Content is WelcomePage)
            {
                first.IsClosable = true;
                LoadFileIntoTab(first, file);
                return;
            }

            var tab = new TabViewItem
            {
                Header = file.DisplayName,
                IconSource = new Microsoft.UI.Xaml.Controls.SymbolIconSource { Symbol = Symbol.Document },
                IsClosable = true
            };
            LoadFileIntoTab(tab, file);
            PdfTabView.TabItems.Add(tab);
            PdfTabView.SelectedItem = tab;
        }

        private static void LoadFileIntoTab(TabViewItem tab, StorageFile file)
        {
            tab.Header = file.DisplayName;
            var viewer = new PdfViewerPage();
            tab.Content = viewer;
            viewer.LoadFile(file);
        }

        // ── 事件处理 ──────────────────────────────────────────────

        private async void TabView_AddTabButtonClick(TabView sender, object args)
            => await PickAndOpenFile();

        private void TabView_TabCloseRequested(TabView sender,
            TabViewTabCloseRequestedEventArgs args)
        {
            sender.TabItems.Remove(args.Tab);
            if (sender.TabItems.Count == 0)
                AddWelcomeTab();
        }

        public async System.Threading.Tasks.Task PickAndOpenFile()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".pdf");
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file != null) OpenFileInNewTab(file);
        }

        public async void OpenExternalLink(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton link && link.Tag is string url)
            {
                var dialog = new Dialogs.ExternalOpenDialog();
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
        }

        public void ApplySettings()
        {
            ElementSoundPlayer.State = SettingsManager.Instance.EnableSound
                ? ElementSoundPlayerState.On
                : ElementSoundPlayerState.Off;
        }

        private static string GetString(string key)
        {
            try { return new Windows.ApplicationModel.Resources.ResourceLoader().GetString(key); }
            catch { return key; }
        }
    }
}
