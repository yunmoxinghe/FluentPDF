using System;
using System.Linq;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using FluentPDF.Pages;
using TabView = Microsoft.UI.Xaml.Controls.TabView;
using TabViewItem = Microsoft.UI.Xaml.Controls.TabViewItem;
using TabViewTabCloseRequestedEventArgs = Microsoft.UI.Xaml.Controls.TabViewTabCloseRequestedEventArgs;

namespace FluentPDF
{
    public sealed partial class MainPage : Page
    {
        public static MainPage? Instance { get; private set; }

        // 标记本次启动是否由文件关联触发（跳过欢迎页）
        private bool _launchedByFile = false;

        public MainPage()
        {
            this.InitializeComponent();
            Instance = this;
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _launchedByFile = e.Parameter is string p && p == "fileActivated";
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            AppThemeManager.CustomizeTitleBar();

            if (Window.Current.Content is Windows.UI.Xaml.FrameworkElement rootEl)
                rootEl.ActualThemeChanged += OnRootThemeChanged;

            // 文件关联启动时不显示欢迎页；普通启动根据设置决定
            if (!_launchedByFile && SettingsManager.Instance.ShowWelcomeOnLaunch)
                AddWelcomeTab();
        }

        private static void OnRootThemeChanged(FrameworkElement sender, object args)
            => AppThemeManager.CustomizeTitleBar();

        private void CustomDragRegion_Loaded(object sender, RoutedEventArgs e)
        {
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;
            coreTitleBar.LayoutMetricsChanged += CoreTitleBar_LayoutMetricsChanged;

            UpdateTitleBarInsets(coreTitleBar);
            Window.Current.SetTitleBar(CustomDragRegion);

            // 监听窗口大小变化，确保最大化 ↔ 窗口化切换时 inset 及时刷新
            Window.Current.SizeChanged += Window_SizeChanged;
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            UpdateTitleBarInsets(coreTitleBar);
        }

        private void CoreTitleBar_LayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
            => UpdateTitleBarInsets(sender);

        private void UpdateTitleBarInsets(CoreApplicationViewTitleBar coreTitleBar)
        {
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

            // TabView 的默认样式通过 Padding="{ThemeResource TabViewHeaderPadding}"
            // 在标签栏顶部加了 8px，用于窗口化时避免内容被系统边框遮挡。
            // 最大化时不需要这个补偿，直接将 Padding 顶部清零。
            var appView = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
            bool isMaximized = appView.IsFullScreenMode || IsWindowMaximized();
            PdfTabView.Padding = isMaximized
                ? new Thickness(0)
                : new Thickness(0, 8, 0, 0);
        }

        private static bool IsWindowMaximized()
        {
            // UWP 中最大化窗口会同时贴近左右显示边缘
            var appView = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
            return appView.AdjacentToLeftDisplayEdge && appView.AdjacentToRightDisplayEdge;
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
                IconSource = new Microsoft.UI.Xaml.Controls.SymbolIconSource { Symbol = Symbol.Home },
                IsClosable = false,
                Content = welcome
            };
            PdfTabView.TabItems.Add(tab);
            PdfTabView.SelectedItem = tab;
        }

        public void OpenFileInNewTab(StorageFile file)
        {
            var tab = new TabViewItem
            {
                Header = file.DisplayName,
                IconSource = new Microsoft.UI.Xaml.Controls.FontIconSource { Glyph = "\uEA90" },
                IsClosable = true
            };
            LoadFileIntoTab(tab, file);
            PdfTabView.TabItems.Add(tab);
            PdfTabView.SelectedItem = tab;

            // 找到欢迎标签
            var welcome = PdfTabView.TabItems
                .OfType<TabViewItem>()
                .FirstOrDefault(t => t.Content is WelcomePage);

            if (welcome != null)
            {
                if (SettingsManager.Instance.AllowCloseWelcomeWhenFileOpen)
                {
                    // 允许关闭：把欢迎标签变为可关闭，但不自动移除
                    welcome.IsClosable = true;
                }
                // 若不允许关闭，保持原样（IsClosable = false）
            }
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
            if (sender.TabItems.Count == 0 && SettingsManager.Instance.ShowWelcomeOnLaunch)
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
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
                OpenFileInNewTab(file);
        }

        // ── 拖拽支持 ──────────────────────────────────────────────

        private void MainPage_DragOver(object sender, DragEventArgs e)
        {
            // 检查是否包含文件，且至少有一个 PDF
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = GetString("DragDrop_OpenPDF");
                e.DragUIOverride.IsGlyphVisible = true;
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        private async void MainPage_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var pdfFiles = items
                    .OfType<StorageFile>()
                    .Where(f => f.FileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var file in pdfFiles)
                    OpenFileInNewTab(file);
            }
            finally
            {
                deferral.Complete();
            }
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

            bool schoolMode = SettingsManager.Instance.SchoolMode;

            foreach (var item in PdfTabView.TabItems)
            {
                if (item is TabViewItem tab && tab.Content is Pages.PdfViewerPage viewer)
                    viewer.ApplySchoolMode(schoolMode);
            }
        }

        private static string GetString(string key)
        {
            try { return new Windows.ApplicationModel.Resources.ResourceLoader().GetString(key); }
            catch { return key; }
        }
    }
}
