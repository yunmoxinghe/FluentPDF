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
    // ── 设计原则 ──────────────────────────────────────────────────────────
    // 页面 Grid 的 Width/Height 始终等于 OriginalSize（原始 PDF 尺寸，单位 px）。
    // 缩放完全由 ScrollViewer.ZoomFactor 驱动，系统自动保持视口中心位置，
    // 不需要任何手动偏移计算，彻底消除位置闪烁。
    //
    // 渲染分辨率 = OriginalSize * ZoomFactor，在 ZoomFactor 稳定后（ViewChanged
    // IsIntermediate=false）按当前 ZoomFactor 重新渲染高清位图。
    // ─────────────────────────────────────────────────────────────────────

    public sealed partial class MainPage : Page
    {
        private readonly ObservableCollection<PdfPageItem> _pages = new();
        private PdfDocument? _doc;
        private CancellationTokenSource? _renderCts;

        // ScrollViewer.ZoomFactor 就是缩放倍率，不再维护单独的 _zoom
        private const double ZoomStep = 0.25;
        private const double ZoomMin = 0.25;
        private const double ZoomMax = 4.0;

        public MainPage()
        {
            InitializeComponent();
            PagesControl.ItemsSource = _pages;
            PdfScrollViewer.ViewChanged += OnViewChanged;
            SetupTitleBar();
        }

        private void PdfScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PagesHost.MinWidth = PdfScrollViewer.ViewportWidth / PdfScrollViewer.ZoomFactor;
        }

        private void SetupTitleBar()
        {
            Window.Current.SetTitleBar(TitleBarArea);
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

                // 计算初始 ZoomFactor：让第一页填满视口宽度
                double viewportWidth = PdfScrollViewer.ActualWidth > 0
                    ? PdfScrollViewer.ActualWidth : ActualWidth;

                float initialZoom = 1.0f;
                using (var firstPage = doc.GetPage(0))
                {
                    if (viewportWidth > 0 && firstPage.Size.Width > 0)
                        initialZoom = (float)Math.Clamp(
                            viewportWidth / firstPage.Size.Width, ZoomMin, ZoomMax);
                }

                // 页面 Grid 尺寸 = 原始 PDF 尺寸（ZoomFactor 负责视觉缩放）
                for (uint i = 0; i < doc.PageCount; i++)
                {
                    using PdfPage page = doc.GetPage(i);
                    _pages.Add(new PdfPageItem
                    {
                        PageIndex = i,
                        DisplayWidth = page.Size.Width,
                        DisplayHeight = page.Size.Height,
                    });
                }

                LoadingRing.IsActive = false;
                PdfScrollViewer.Visibility = Visibility.Visible;
                Toolbar.Visibility = Visibility.Visible;

                // 设置初始缩放（触发 ViewChanged → RenderVisiblePagesAsync）
                PdfScrollViewer.ChangeView(null, null, initialZoom, disableAnimation: true);
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

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            float cur = PdfScrollViewer.ZoomFactor;
            float next = (float)Math.Min(Math.Round(cur / ZoomStep + 1) * ZoomStep, ZoomMax);
            PdfScrollViewer.ChangeView(null, null, next, disableAnimation: true);
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            float cur = PdfScrollViewer.ZoomFactor;
            float next = (float)Math.Max(Math.Round(cur / ZoomStep - 1) * ZoomStep, ZoomMin);
            PdfScrollViewer.ChangeView(null, null, next, disableAnimation: true);
        }

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pages.Count == 0) return;
            float fit = (float)Math.Clamp(
                PdfScrollViewer.ActualWidth / _pages[0].DisplayWidth, ZoomMin, ZoomMax);
            PdfScrollViewer.ChangeView(null, null, fit, disableAnimation: true);
        }

        // ── 按需渲染 ──────────────────────────────────────────────────────

        private async void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate) return;
            await RenderVisiblePagesAsync();
        }

        private async Task RenderVisiblePagesAsync()
        {
            if (_doc == null || _pages.Count == 0) return;

            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();
            var token = _renderCts.Token;

            float zoom = PdfScrollViewer.ZoomFactor;

            // 视口在内容坐标系中的范围（内容坐标 = 像素 / ZoomFactor）
            double viewTop = PdfScrollViewer.VerticalOffset / zoom;
            double viewBottom = viewTop + PdfScrollViewer.ViewportHeight / zoom;

            // 按 DisplayHeight（原始尺寸）累加定位每页
            double y = 0;
            const double margin = 8.0; // 与 XAML Margin="8,8,8,8" 一致
            int firstVisible = -1, lastVisible = -1;
            for (int i = 0; i < _pages.Count; i++)
            {
                double pageBottom = y + _pages[i].DisplayHeight + margin * 2;
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

            // 从视口中心开始向两边扩散渲染，让用户当前看到的页面最先更新
            int center = (firstVisible + lastVisible) / 2;
            var renderOrder = new System.Collections.Generic.List<int>();
            
            // 先加中心页
            renderOrder.Add(center);
            
            // 交替加左右两侧，直到覆盖 [from, to] 范围
            int left = center - 1;
            int right = center + 1;
            while (left >= from || right <= to)
            {
                if (right <= to) renderOrder.Add(right++);
                if (left >= from) renderOrder.Add(left--);
            }

            foreach (int i in renderOrder)
            {
                if (token.IsCancellationRequested) return;
                var item = _pages[i];

                // 只在缩放变化超过阈值时重渲染，避免微小滚动触发不必要的渲染
                if (item.PageImage != null &&
                    Math.Abs(zoom - item.RenderedAtZoom) < 0.05f) continue;

                try
                {
                    using PdfPage page = _doc.GetPage(item.PageIndex);
                    uint w = (uint)Math.Max(1, Math.Round(item.DisplayWidth * zoom));
                    uint h = (uint)Math.Max(1, Math.Round(item.DisplayHeight * zoom));
                    var opts = new PdfPageRenderOptions
                    {
                        DestinationWidth = w,
                        DestinationHeight = h
                    };
                    var stream = new InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(stream, opts);
                    if (token.IsCancellationRequested) return;

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    if (token.IsCancellationRequested) return;

                    item.SetImage(bitmap, zoom);
                }
                catch (OperationCanceledException) { return; }
                catch { /* 单页失败不影响其他页 */ }
            }
        }
    }

    public sealed class PdfPageItem : INotifyPropertyChanged
    {
        public uint PageIndex { get; set; }

        // 页面 Grid 的固定尺寸（原始 PDF 尺寸），不随缩放变化
        public double DisplayWidth { get; set; }
        public double DisplayHeight { get; set; }

        // 记录当前位图是在哪个 ZoomFactor 下渲染的
        public float RenderedAtZoom { get; private set; } = 0f;

        private BitmapImage? _pageImage;
        public BitmapImage? PageImage
        {
            get => _pageImage;
            private set
            {
                _pageImage = value;
                OnPropertyChanged(nameof(PageImage));
                OnPropertyChanged(nameof(IsFirstLoad));
            }
        }

        public bool IsFirstLoad => _pageImage == null;

        public void SetImage(BitmapImage bitmap, float zoom)
        {
            RenderedAtZoom = zoom;
            PageImage = bitmap;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
