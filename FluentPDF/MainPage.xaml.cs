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
            // 延伸内容到标题栏，并用 LayoutMetricsChanged 同步两侧占位宽度
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;
            coreTitleBar.LayoutMetricsChanged += CoreTitleBar_LayoutMetricsChanged;

            // 拖动区域叠加在页面顶部，高度跟随 TabView 标签栏实际高度
            Window.Current.SetTitleBar(TitleBarDragGrid);
            PdfTabView.SizeChanged += (s, args) => SyncDragGridHeight();

            // 初始同步一次
            UpdateTitleBarLayout(coreTitleBar);

            AppThemeManager.CustomizeTitleBar();

            // 启动时显示欢迎标签
            AddWelcomeTab();
        }

        // 通过可视树找到 TabView 内部的标签栏，取其实际高度同步给拖拽区
        private void SyncDragGridHeight()
        {
            var tabStrip = FindTabStrip(PdfTabView);
            if (tabStrip != null)
                TitleBarDragGrid.Height = tabStrip.ActualHeight;
        }

        private static Windows.UI.Xaml.FrameworkElement? FindTabStrip(
            Windows.UI.Xaml.DependencyObject parent)
        {
            int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Windows.UI.Xaml.Controls.ListView lv &&
                    lv.GetType().Name == "TabViewListView")
                    return lv;
                var result = FindTabStrip(child);
                if (result != null) return result;
            }
            return null;
        }

        private void CoreTitleBar_LayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
            => UpdateTitleBarLayout(sender);

        private void UpdateTitleBarLayout(CoreApplicationViewTitleBar coreTitleBar)
        {
            // 根据流向决定哪侧是系统按钮
            if (FlowDirection == FlowDirection.LeftToRight)
            {
                ShellTitlebarInset.MinWidth = coreTitleBar.SystemOverlayLeftInset;
                LeftInsetColumn.Width  = new GridLength(coreTitleBar.SystemOverlayLeftInset);
                RightInsetColumn.Width = new GridLength(coreTitleBar.SystemOverlayRightInset);
                SystemButtonInset.MinWidth = coreTitleBar.SystemOverlayRightInset;
            }
            else
            {
                ShellTitlebarInset.MinWidth = coreTitleBar.SystemOverlayRightInset;
                LeftInsetColumn.Width  = new GridLength(coreTitleBar.SystemOverlayRightInset);
                RightInsetColumn.Width = new GridLength(coreTitleBar.SystemOverlayLeftInset);
                SystemButtonInset.MinWidth = coreTitleBar.SystemOverlayLeftInset;
            }

            ShellTitlebarInset.Height = coreTitleBar.Height;
            SystemButtonInset.Height  = coreTitleBar.Height;
            // TitleBarDragGrid 高度由 SyncDragGridHeight() 跟随标签栏实际高度，不在此设置
        }

        // ── 从 App.OnFileActivated 调用 ───────────────────────────

        public void OpenFile(StorageFile? file)
        {
            if (file != null) OpenFileInNewTab(file);
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
