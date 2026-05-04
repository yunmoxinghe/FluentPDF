using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Pages
{
    public sealed partial class PdfViewerPage : Page
    {
        private readonly ObservableCollection<PdfPageItem> _pages = new();
        private PdfDocument? _doc;
        private CancellationTokenSource? _renderCts;

        private const double ZoomStep = 0.25;
        private const double ZoomMin = 0.25;
        private const double ZoomMax = 4.0;

        public PdfViewerPage()
        {
            this.InitializeComponent();
            PagesControl.ItemsSource = _pages;
            PdfScrollViewer.ViewChanged += OnViewChanged;
        }

        // ── 公开接口：由 PdfTabPage 调用 ─────────────────────────

        public async void LoadFile(StorageFile file)
        {
            LoadingRing.IsActive = true;
            PdfScrollViewer.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;

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

                double viewportWidth = PdfScrollViewer.ActualWidth > 0
                    ? PdfScrollViewer.ActualWidth : ActualWidth;

                float initialZoom = 1.0f;
                using (var firstPage = doc.GetPage(0))
                {
                    if (viewportWidth > 0 && firstPage.Size.Width > 0)
                        initialZoom = (float)Math.Clamp(
                            viewportWidth / firstPage.Size.Width, ZoomMin, ZoomMax);
                }

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

                PdfScrollViewer.ChangeView(null, null, initialZoom, disableAnimation: true);
            }
            catch (Exception ex)
            {
                LoadingRing.IsActive = false;
                var dialog = new ContentDialog
                {
                    Title = "无法加载 PDF",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
        }

        // ── 缩放控件 ──────────────────────────────────────────────

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

        private void PdfScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PagesHost.MinWidth = PdfScrollViewer.ViewportWidth / PdfScrollViewer.ZoomFactor;
        }

        // ── 按需渲染 ──────────────────────────────────────────────

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
            double viewTop = PdfScrollViewer.VerticalOffset / zoom;
            double viewBottom = viewTop + PdfScrollViewer.ViewportHeight / zoom;

            double y = 0;
            const double margin = 8.0;
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

            // 从视口中心向两边扩散渲染
            int center = (firstVisible + lastVisible) / 2;
            var renderOrder = new List<int> { center };
            int left = center - 1, right = center + 1;
            while (left >= from || right <= to)
            {
                if (right <= to) renderOrder.Add(right++);
                if (left >= from) renderOrder.Add(left--);
            }

            foreach (int i in renderOrder)
            {
                if (token.IsCancellationRequested) return;
                var item = _pages[i];
                if (item.PageImage != null && Math.Abs(zoom - item.RenderedAtZoom) < 0.05f) continue;

                try
                {
                    using PdfPage page = _doc.GetPage(item.PageIndex);
                    uint w = (uint)Math.Max(1, Math.Round(item.DisplayWidth * zoom));
                    uint h = (uint)Math.Max(1, Math.Round(item.DisplayHeight * zoom));
                    var opts = new PdfPageRenderOptions { DestinationWidth = w, DestinationHeight = h };
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

    // ── 数据模型 ──────────────────────────────────────────────────

    public sealed class PdfPageItem : INotifyPropertyChanged
    {
        public uint PageIndex { get; set; }
        public double DisplayWidth { get; set; }
        public double DisplayHeight { get; set; }
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
