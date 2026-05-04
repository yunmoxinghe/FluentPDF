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

        // ── 渲染任务控制 ───────────────────────────────────────────
        // 两个独立的 CTS，优先级从高到低：Layer2 > Layer1
        private CancellationTokenSource? _layer2Cts;   // 高分辨率（可见区）
        private CancellationTokenSource? _layer1Cts;   // 单页缩略图（后台）
        private DispatcherTimer?         _debounceTimer;
        private const int DebounceMs = 150;

        // ── 缓存 ──────────────────────────────────────────────────
        // Layer2 LRU 缓存：key=(pageIndex, zoomBucket)，最多 60 页
        private readonly LruCache<(uint, int), BitmapImage> _layer2Cache = new(60);
        // Layer1 缓存：全量，每页一张缩略图
        private readonly Dictionary<uint, BitmapImage> _layer1Cache = new();

        // ── 缩放常量 ──────────────────────────────────────────────
        private const double ZoomStep           = 0.25;
        private const double ZoomMin            = 0.25;
        private const double ZoomMax            = 4.0;
        private const int    PreloadAhead       = 4;   // 滚动方向前方预加载页数
        private const int    PreloadBehind      = 2;   // 滚动方向后方预加载页数
        private const uint   Layer1Width        = 160; // Layer1 每页缩略图宽度（px）
        private const uint   Layer2MaxDim       = 3000;// Layer2 单边最大像素，防止超高分辨率渲染

        // ── 滚动方向追踪 ──────────────────────────────────────────
        private double _lastVerticalOffset = 0;
        private int    _scrollDirection    = 0; // +1 向下，-1 向上，0 未知

        private StorageFile? _pendingFile;

        public PdfViewerPage()
        {
            this.InitializeComponent();
            PagesRepeater.ItemsSource = _pages;
            PdfScrollViewer.ViewChanged += OnViewChanged;
        }

        // ── 公开接口 ──────────────────────────────────────────────

        public void LoadFile(StorageFile file)
        {
            _pendingFile = file;
            if (ActualWidth > 0 || ActualHeight > 0) { _ = LoadFileAsync(file); return; }
            Loaded        += OnLoadedThenLoadFile;
            LayoutUpdated += OnLayoutUpdatedThenLoadFile;
        }

        private void OnLoadedThenLoadFile(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoadedThenLoadFile; LayoutUpdated -= OnLayoutUpdatedThenLoadFile;
            if (_pendingFile != null) _ = LoadFileAsync(_pendingFile);
        }

        private void OnLayoutUpdatedThenLoadFile(object? sender, object e)
        {
            if (ActualWidth <= 0 && ActualHeight <= 0) return;
            Loaded -= OnLoadedThenLoadFile; LayoutUpdated -= OnLayoutUpdatedThenLoadFile;
            if (_pendingFile != null) _ = LoadFileAsync(_pendingFile);
        }

        // ── 加载流程 ──────────────────────────────────────────────

        private async Task LoadFileAsync(StorageFile file)
        {
            _pendingFile = null;
            LoadingRing.IsActive       = true;
            PdfScrollViewer.Visibility = Visibility.Collapsed;
            Toolbar.Visibility         = Visibility.Collapsed;

            CancelAll();
            _pages.Clear();
            _layer2Cache.Clear();
            _layer1Cache.Clear();
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
                using (var fp = doc.GetPage(0))
                {
                    if (viewportWidth > 0 && fp.Size.Width > 0)
                        initialZoom = (float)Math.Clamp(viewportWidth / fp.Size.Width, ZoomMin, ZoomMax);
                    _pages.Add(new PdfPageItem
                    {
                        PageIndex     = 0,
                        DisplayWidth  = fp.Size.Width,
                        DisplayHeight = fp.Size.Height,
                    });
                }

                LoadingRing.IsActive       = false;
                PdfScrollViewer.Visibility = Visibility.Visible;
                Toolbar.Visibility         = Visibility.Visible;
                PdfScrollViewer.ChangeView(null, null, initialZoom, disableAnimation: true);

                // 立即渲染可见区高分辨率
                ScheduleLayer2();

                // 后台补全剩余页元数据
                if (doc.PageCount > 1)
                    await LoadRemainingMetaAsync(doc);

                // 启动后台 Layer1 缩略图渲染
                StartBackgroundLayer1();
            }
            catch (Exception ex)
            {
                LoadingRing.IsActive = false;
                var dialog = new ContentDialog
                {
                    Title = "无法加载 PDF", Content = ex.Message,
                    CloseButtonText = "确定", XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private async Task LoadRemainingMetaAsync(PdfDocument doc)
        {
            const int batchSize = 20;
            var batch = new List<PdfPageItem>(batchSize);
            for (uint i = 1; i < doc.PageCount; i++)
            {
                using PdfPage page = doc.GetPage(i);
                batch.Add(new PdfPageItem
                {
                    PageIndex     = i,
                    DisplayWidth  = page.Size.Width,
                    DisplayHeight = page.Size.Height,
                });
                if (batch.Count >= batchSize || i == doc.PageCount - 1)
                {
                    var toAdd = batch.ToArray(); batch.Clear();
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                    {
                        foreach (var item in toAdd) _pages.Add(item);
                    });
                    await Task.Yield();
                }
            }
        }

        // ── 后台 Layer1 渲染 ──────────────────────────────────────

        private void StartBackgroundLayer1()
        {
            _layer1Cts = new CancellationTokenSource();
            _ = RenderLayer1Async(_layer1Cts.Token);
        }

        /// <summary>
        /// Layer1：逐页渲染单页缩略图（Layer1Width px 宽）。
        /// 可见区域的页优先渲染，其余按顺序。
        /// </summary>
        private async Task RenderLayer1Async(CancellationToken token)
        {
            if (_doc == null) return;

            var order = BuildLayer1Order();

            foreach (uint i in order)
            {
                if (token.IsCancellationRequested) return;
                if (_layer1Cache.ContainsKey(i)) continue;
                // 已有 Layer2 则跳过
                if (i < (uint)_pages.Count && _pages[(int)i].IsLayer2Ready) continue;

                try
                {
                    using PdfPage page = _doc.GetPage(i);
                    double aspect = page.Size.Height / page.Size.Width;
                    uint w = Layer1Width;
                    uint h = (uint)Math.Max(1, Math.Round(w * aspect));

                    var opts = new PdfPageRenderOptions { DestinationWidth = w, DestinationHeight = h };
                    BitmapImage bitmap;
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        await page.RenderToStreamAsync(stream, opts);
                        if (token.IsCancellationRequested) return;
                        bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(stream);
                    }
                    if (token.IsCancellationRequested) return;

                    _layer1Cache[i] = bitmap;
                    if (i < (uint)_pages.Count)
                        _pages[(int)i].SetLayer1(bitmap);
                }
                catch (OperationCanceledException) { return; }
                catch { }

                await Task.Yield();
            }
        }

        private List<uint> BuildLayer1Order()
        {
            var (first, last) = GetVisibleRange();
            var order = new List<uint>((int)(_doc?.PageCount ?? 0));
            // 可见页先加
            for (uint i = (uint)Math.Max(0, first); i <= (uint)Math.Min(_pages.Count - 1, last); i++)
                order.Add(i);
            // 其余页按页码顺序
            for (uint i = 0; i < (_doc?.PageCount ?? 0); i++)
                if (i < (uint)Math.Max(0, first) || i > (uint)Math.Min(_pages.Count - 1, last))
                    order.Add(i);
            return order;
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
            double vw = PdfScrollViewer.ViewportWidth;
            double w  = vw > 0 ? vw : PdfScrollViewer.ActualWidth;
            PagesHost.MinWidth = w;
            PagesHost.Width    = w;
        }

        // ── 视图变化调度 ──────────────────────────────────────────

        private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            // 记录滚动方向
            double currentOffset = PdfScrollViewer.VerticalOffset;
            if (currentOffset > _lastVerticalOffset)      _scrollDirection = 1;   // 向下
            else if (currentOffset < _lastVerticalOffset) _scrollDirection = -1;  // 向上
            _lastVerticalOffset = currentOffset;

            CheckAndPreemptForViewport();

            if (!e.IsIntermediate)
            {
                _debounceTimer?.Stop();
                _debounceTimer = new DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
                _debounceTimer.Tick += (s, _) =>
                {
                    _debounceTimer!.Stop();
                    ScheduleLayer2();
                };
                _debounceTimer.Start();
            }
        }

        private void CheckAndPreemptForViewport()
        {
            if (_doc == null || _pages.Count == 0) return;
            var (first, last) = GetVisibleRange();
            if (first < 0) return;

            bool needsLayer2 = false;
            for (int i = first; i <= last; i++)
            {
                if (i >= _pages.Count) break;
                if (!_pages[i].IsLayer2Ready) { needsLayer2 = true; break; }
            }

            if (needsLayer2)
            {
                _layer1Cts?.Cancel();
                _layer1Cts = null;
                ScheduleLayer2();
            }
        }

        private void ScheduleLayer2()
        {
            _layer2Cts?.Cancel();
            _layer2Cts = new CancellationTokenSource();
            _ = RenderLayer2Async(_layer2Cts.Token);
        }

        // ── Layer2 高分辨率渲染 ───────────────────────────────────

        private async Task RenderLayer2Async(CancellationToken token)
        {
            if (_doc == null || _pages.Count == 0) return;

            float zoom = PdfScrollViewer.ZoomFactor;
            var (firstVisible, lastVisible) = GetVisibleRange();
            if (firstVisible < 0) return;

            // 根据滚动方向决定前后预加载范围
            int ahead  = _scrollDirection >= 0 ? PreloadAhead  : PreloadBehind;
            int behind = _scrollDirection >= 0 ? PreloadBehind : PreloadAhead;
            int from   = Math.Max(0, firstVisible - behind);
            int to     = Math.Min(_pages.Count - 1, lastVisible + ahead);

            int center     = (firstVisible + lastVisible) / 2;
            var order      = BuildRenderOrder(center, from, to);
            int zoomBucket = ZoomToBucket(zoom);

            // 降级范围外的页面（释放 Layer2，回退到 Layer1）
            for (int i = 0; i < _pages.Count; i++)
            {
                if (i < from || i > to)
                    _pages[i].EvictLayer2(_layer1Cache);
            }

            // 第一轮：只渲染可见区
            foreach (int i in order)
            {
                if (token.IsCancellationRequested) return;
                var item = _pages[i];

                if (_layer2Cache.TryGet((item.PageIndex, zoomBucket), out var cached))
                {
                    item.SetLayer2(cached!);
                    continue;
                }

                if (i < firstVisible || i > lastVisible)
                    continue;

                await RenderLayer2PageAsync(item, zoom, zoomBucket, token);
            }

            if (token.IsCancellationRequested) return;

            // 第二轮：补全预加载范围
            foreach (int i in order)
            {
                if (token.IsCancellationRequested) return;
                var item = _pages[i];
                if (item.IsLayer2Ready) continue;

                if (_layer2Cache.TryGet((item.PageIndex, zoomBucket), out var cached))
                {
                    item.SetLayer2(cached!);
                    continue;
                }

                await RenderLayer2PageAsync(item, zoom, zoomBucket, token);
            }

            // Layer2 完成后，恢复后台 Layer1（如果被抢占过）
            if (!token.IsCancellationRequested)
                ResumeBackgroundLayer1();
        }

        private async Task RenderLayer2PageAsync(
            PdfPageItem item, float zoom, int zoomBucket, CancellationToken token)
        {
            try
            {
                using PdfPage page = _doc!.GetPage(item.PageIndex);
                uint w = (uint)Math.Max(1, Math.Round(item.DisplayWidth  * zoom));
                uint h = (uint)Math.Max(1, Math.Round(item.DisplayHeight * zoom));

                // 限制最大渲染分辨率，防止高缩放时渲染超大位图
                if (w > Layer2MaxDim || h > Layer2MaxDim)
                {
                    double scale = Math.Min((double)Layer2MaxDim / w, (double)Layer2MaxDim / h);
                    w = (uint)Math.Max(1, Math.Round(w * scale));
                    h = (uint)Math.Max(1, Math.Round(h * scale));
                }

                var opts = new PdfPageRenderOptions { DestinationWidth = w, DestinationHeight = h };
                BitmapImage bitmap;
                using (var stream = new InMemoryRandomAccessStream())
                {
                    await page.RenderToStreamAsync(stream, opts);
                    if (token.IsCancellationRequested) return;
                    bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                }
                if (token.IsCancellationRequested) return;

                _layer2Cache.Put((item.PageIndex, zoomBucket), bitmap);
                item.SetLayer2(bitmap);
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private void ResumeBackgroundLayer1()
        {
            if (_layer1Cts != null) return; // 已在运行
            bool needsLayer1 = false;
            for (int i = 0; i < _pages.Count; i++)
                if (!_pages[i].HasLayer1) { needsLayer1 = true; break; }

            if (needsLayer1)
            {
                _layer1Cts = new CancellationTokenSource();
                _ = RenderLayer1Async(_layer1Cts.Token);
            }
        }

        // ── 工具方法 ──────────────────────────────────────────────

        private (int first, int last) GetVisibleRange()
        {
            if (_pages.Count == 0) return (-1, -1);
            float  zoom       = PdfScrollViewer.ZoomFactor;
            double viewTop    = PdfScrollViewer.VerticalOffset / zoom;
            double viewBottom = viewTop + PdfScrollViewer.ViewportHeight / zoom;

            double y = 0;
            const double spacing = 16.0;
            int first = -1, last = -1;
            for (int i = 0; i < _pages.Count; i++)
            {
                double bottom = y + _pages[i].DisplayHeight;
                if (bottom >= viewTop && y <= viewBottom)
                {
                    if (first < 0) first = i;
                    last = i;
                }
                y = bottom + spacing;
            }
            return (first, last);
        }

        private static int ZoomToBucket(float zoom)
            => (int)Math.Round(zoom / 0.25f);

        private static List<int> BuildRenderOrder(int center, int from, int to)
        {
            var order = new List<int> { center };
            int left = center - 1, right = center + 1;
            while (left >= from || right <= to)
            {
                if (right <= to) order.Add(right++);
                if (left >= from) order.Add(left--);
            }
            return order;
        }

        private void CancelAll()
        {
            _debounceTimer?.Stop();
            _layer2Cts?.Cancel(); _layer2Cts = null;
            _layer1Cts?.Cancel(); _layer1Cts = null;
        }

        public bool TryGetThumb(uint pageIndex, out BitmapImage? thumb)
            => _layer1Cache.TryGetValue(pageIndex, out thumb);
    }

    // ── 数据模型 ──────────────────────────────────────────────────

    public sealed class PdfPageItem : INotifyPropertyChanged
    {
        public uint   PageIndex     { get; set; }
        public double DisplayWidth  { get; set; }
        public double DisplayHeight { get; set; }

        // ── 双层各自的双缓冲 ──────────────────────────────────────
        // 每层 Back + Front，切换时旧图留在 Back 兜底，新图写入 Front，
        // 下一帧通过 Dispatcher.Low 清除 Back，避免任何空白帧。

        private BitmapImage? _l1Back,  _l1Front;
        private BitmapImage? _l2Back,  _l2Front;

        public BitmapImage? Layer1Back  { get => _l1Back;  private set { _l1Back  = value; Notify(nameof(Layer1Back));  } }
        public BitmapImage? Layer1Front { get => _l1Front; private set { _l1Front = value; Notify(nameof(Layer1Front)); } }
        public BitmapImage? Layer2Back  { get => _l2Back;  private set { _l2Back  = value; Notify(nameof(Layer2Back));  } }
        public BitmapImage? Layer2Front { get => _l2Front; private set { _l2Front = value; Notify(nameof(Layer2Front)); } }

        public bool HasLayer1     => _l1Front != null;
        public bool IsLayer2Ready => _l2Front != null;

        public void SetLayer1(BitmapImage bmp) => SwapLayer(bmp, ref _l1Back, ref _l1Front,
            nameof(Layer1Back), nameof(Layer1Front));

        public void SetLayer2(BitmapImage bmp) => SwapLayer(bmp, ref _l2Back, ref _l2Front,
            nameof(Layer2Back), nameof(Layer2Front));

        /// <summary>
        /// 释放 Layer2，降级回 Layer1。
        /// 如果有 Layer1 缓存则用 Layer1 填充 Layer2 的位置（视觉上无缝）。
        /// </summary>
        public void EvictLayer2(Dictionary<uint, BitmapImage> layer1Cache)
        {
            if (_l2Front == null) return;
            if (layer1Cache.TryGetValue(PageIndex, out var l1))
                SwapLayer(l1, ref _l2Back, ref _l2Front, nameof(Layer2Back), nameof(Layer2Front));
            else
                ClearLayerImmediate(ref _l2Back, ref _l2Front, nameof(Layer2Back), nameof(Layer2Front));
        }

        private void SwapLayer(BitmapImage bmp,
            ref BitmapImage? back, ref BitmapImage? front,
            string backName, string frontName)
        {
            if (front != null)
            {
                back = front;
                Notify(backName);
            }
            front = bmp;
            Notify(frontName);

            if (back != null)
            {
                var backRef = backName;
                _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher
                    .RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                    {
                        ClearBackByName(backRef);
                    });
            }
        }

        private void ClearBackByName(string name)
        {
            switch (name)
            {
                case nameof(Layer1Back): Layer1Back = null; break;
                case nameof(Layer2Back): Layer2Back = null; break;
            }
        }

        private void ClearLayerImmediate(
            ref BitmapImage? back, ref BitmapImage? front,
            string backName, string frontName)
        {
            back  = null; Notify(backName);
            front = null; Notify(frontName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── LRU 缓存 ──────────────────────────────────────────────────

    internal sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map;
        private readonly LinkedList<(TKey key, TValue value)> _list;

        public LruCache(int capacity)
        {
            _capacity = capacity;
            _map  = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
            _list = new LinkedList<(TKey, TValue)>();
        }

        public bool TryGet(TKey key, out TValue? value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node); _list.AddFirst(node);
                value = node.Value.value; return true;
            }
            value = default; return false;
        }

        public void Put(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing); _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                var last = _list.Last!;
                _map.Remove(last.Value.key); _list.RemoveLast();
            }
            var node = _list.AddFirst((key, value));
            _map[key] = node;
        }

        public void Clear() { _map.Clear(); _list.Clear(); }
    }
}
