using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace FluentPDF
{
    public sealed partial class MainPage : Page
    {
        private readonly ObservableCollection<PdfPageItem> _pages = new();
        private PdfDocument? _doc;
        private CancellationTokenSource? _renderCts;

        // 当前缩放倍率（1.0 = 原始尺寸）
        private double _zoom = 1.0;
        private double _lastRenderedZoom = 1.0; // 上次渲染时的缩放倍率
        private const double ZoomStep = 0.25;
        private const double ZoomMin = 0.25;
        private const double ZoomMax = 4.0;
        private const double PageMargin = 16.0; // 与 XAML Margin 一致

        public MainPage()
        {
            InitializeComponent();
            PagesControl.ItemsSource = _pages;
            PdfScrollViewer.ViewChanged += OnViewChanged;
            SetupTitleBar();
        }

        private void SetupTitleBar()
        {
            // 注册可拖动区域
            Window.Current.SetTitleBar(TitleBarArea);

            // 同步标题栏高度（DPI 变化时也跟着更新）
            var coreTitleBar = Windows.ApplicationModel.Core.CoreApplication.GetCurrentView().TitleBar;
            TitleBarRow.Height = new Windows.UI.Xaml.GridLength(coreTitleBar.Height > 0 ? coreTitleBar.Height : 32);
            coreTitleBar.LayoutMetricsChanged += (s, _) =>
            {
                TitleBarRow.Height = new Windows.UI.Xaml.GridLength(s.Height);
            };
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is StorageFile file)
                await LoadPdfAsync(file);
        }

        // ── 文件打开 ──────────────────────────────────────────────────────

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".pdf");
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file != null) await LoadPdfAsync(file);
        }

        private async Task LoadPdfAsync(StorageFile file)
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            PdfScrollViewer.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;

            _renderCts?.Cancel();
            _pages.Clear();
            _doc = null;

            try
            {
                PdfDocument doc;
                try { doc = await PdfDocument.LoadFromFileAsync(file); }
                catch (Exception ex) when (ex.HResult == unchecked((int)0x8007052b))
                { throw new InvalidOperationException("该 PDF 文件已加密，暂不支持密码保护的文件。"); }
                catch (Exception ex) when (ex.HResult == unchecked((int)0x80004005))
                { throw new InvalidOperationException("文件不是有效的 PDF 文档。"); }

                _doc = doc;

                // 适合宽度：初始缩放让第一页填满视口
                double viewportWidth = PdfScrollViewer.ActualWidth > 0
                    ? PdfScrollViewer.ActualWidth : ActualWidth;

                using (var firstPage = doc.GetPage(0))
                {
                    _zoom = viewportWidth > 0
                        ? Math.Clamp(viewportWidth / firstPage.Size.Width, ZoomMin, ZoomMax)
                        : 1.0;
                }

                // 填充占位符（只存原始尺寸，不渲染位图）
                for (uint i = 0; i < doc.PageCount; i++)
                {
                    using PdfPage page = doc.GetPage(i);
                    _pages.Add(new PdfPageItem
                    {
                        PageIndex = i,
                        OriginalWidth = page.Size.Width,
                        OriginalHeight = page.Size.Height,
                        Zoom = _zoom
                    });
                }

                LoadingRing.IsActive = false;
                PdfScrollViewer.Visibility = Visibility.Visible;
                Toolbar.Visibility = Visibility.Visible;

                await RenderVisiblePagesAsync();
            }
            catch (Exception ex)
            {
                LoadingRing.IsActive = false;
                PlaceholderPanel.Visibility = Visibility.Visible;
                var dialog = new ContentDialog
                {
                    Title = "无法加载 PDF",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
        }

        // ── 缩放控件 ──────────────────────────────────────────────────────

        private async void ZoomInButton_Click(object sender, RoutedEventArgs e)
            => await SetZoomAsync(Math.Min(_zoom + ZoomStep, ZoomMax));

        private async void ZoomOutButton_Click(object sender, RoutedEventArgs e)
            => await SetZoomAsync(Math.Max(_zoom - ZoomStep, ZoomMin));

        private async void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pages.Count == 0) return;
            double fit = Math.Clamp(
                PdfScrollViewer.ActualWidth / _pages[0].OriginalWidth, ZoomMin, ZoomMax);
            await SetZoomAsync(fit);
        }

        private async Task SetZoomAsync(double newZoom)
        {
            if (_doc == null || Math.Abs(newZoom - _zoom) < 0.001) return;

            // 记录缩放前视口中心点的相对位置（百分比）
            double oldZoom = _zoom;
            double centerXRatio = 0.5, centerYRatio = 0.5;

            if (PdfScrollViewer.ScrollableWidth > 0)
                centerXRatio = (PdfScrollViewer.HorizontalOffset + PdfScrollViewer.ViewportWidth / 2)
                    / (PdfScrollViewer.ExtentWidth);

            if (PdfScrollViewer.ScrollableHeight > 0)
                centerYRatio = (PdfScrollViewer.VerticalOffset + PdfScrollViewer.ViewportHeight / 2)
                    / (PdfScrollViewer.ExtentHeight);

            _zoom = newZoom;
            _lastRenderedZoom = _zoom;

            // 更新尺寸但保留旧位图
            foreach (var p in _pages)
            {
                p.Zoom = _zoom;
                p.NeedsRerender = true;
            }

            // 等待布局更新
            PagesControl.UpdateLayout();

            // 恢复到相同的相对位置
            double newCenterX = centerXRatio * PdfScrollViewer.ExtentWidth;
            double newCenterY = centerYRatio * PdfScrollViewer.ExtentHeight;
            double newOffsetX = newCenterX - PdfScrollViewer.ViewportWidth / 2;
            double newOffsetY = newCenterY - PdfScrollViewer.ViewportHeight / 2;

            PdfScrollViewer.ChangeView(newOffsetX, newOffsetY, (float)_zoom, true);

            await RenderVisiblePagesAsync();
        }

        // ── 按需渲染 ──────────────────────────────────────────────────────

        private async void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate) return;

            float svZoom = PdfScrollViewer.ZoomFactor;
            double newZoom = Math.Clamp(svZoom, ZoomMin, ZoomMax);

            if (Math.Abs(newZoom - _lastRenderedZoom) > 0.05)
            {
                _zoom = newZoom;
                // 触摸板手势：ScrollViewer 已维护好位置，只更新渲染分辨率
                // 不调用 ChangeView，避免位置跳动
                foreach (var p in _pages)
                {
                    p.Zoom = _zoom;
                    p.NeedsRerender = true;
                }
                _lastRenderedZoom = _zoom;
            }

            await RenderVisiblePagesAsync();
        }

        private async Task RenderVisiblePagesAsync()
        {
            if (_doc == null || _pages.Count == 0) return;

            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();
            var token = _renderCts.Token;

            double viewTop = PdfScrollViewer.VerticalOffset / PdfScrollViewer.ZoomFactor;
            double viewBottom = viewTop + PdfScrollViewer.ViewportHeight / PdfScrollViewer.ZoomFactor;

            // 按缩放后高度累加定位每页
            double y = 0;
            int firstVisible = -1, lastVisible = -1;
            for (int i = 0; i < _pages.Count; i++)
            {
                double pageBottom = y + _pages[i].RenderedHeight + PageMargin;
                if (pageBottom >= viewTop && y <= viewBottom)
                {
                    if (firstVisible < 0) firstVisible = i;
                    lastVisible = i;
                }
                y = pageBottom;
            }

            if (firstVisible < 0) return;

            int from = Math.Max(0, firstVisible - 1);
            int to = Math.Min(_pages.Count - 1, lastVisible + 1);

            for (int i = from; i <= to; i++)
            {
                if (token.IsCancellationRequested) return;
                var item = _pages[i];
                if (item.PageImage != null && !item.NeedsRerender) continue;

                try
                {
                    using PdfPage page = _doc.GetPage(item.PageIndex);
                    var opts = new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)item.RenderedWidth,
                        DestinationHeight = (uint)item.RenderedHeight
                    };
                    var stream = new InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(stream, opts);
                    if (token.IsCancellationRequested) return;

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    // 渲染完成后原子替换，旧图一直显示到新图就绪
                    item.PageImage = bitmap;
                    item.NeedsRerender = false;
                }
                catch (OperationCanceledException) { return; }
                catch { /* 单页失败不影响其他页 */ }
            }
        }
    }

    public sealed class PdfPageItem : INotifyPropertyChanged
    {
        public uint PageIndex { get; set; }
        public double OriginalWidth { get; set; }
        public double OriginalHeight { get; set; }

        // 标记需要按新分辨率重渲染（但保留旧图，不闪灰）
        public bool NeedsRerender { get; set; }

        private double _zoom = 1.0;
        public double Zoom
        {
            get => _zoom;
            set
            {
                _zoom = value;
                OnPropertyChanged(nameof(Zoom));
                OnPropertyChanged(nameof(RenderedWidth));
                OnPropertyChanged(nameof(RenderedHeight));
            }
        }

        public double RenderedWidth => Math.Round(OriginalWidth * _zoom);
        public double RenderedHeight => Math.Round(OriginalHeight * _zoom);

        private BitmapImage? _pageImage;
        public BitmapImage? PageImage
        {
            get => _pageImage;
            set
            {
                _pageImage = value;
                OnPropertyChanged(nameof(PageImage));
                OnPropertyChanged(nameof(IsFirstLoad));
            }
        }

        // 只在从未渲染过时显示进度环，缩放重渲染时不显示
        public bool IsFirstLoad => _pageImage == null;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
