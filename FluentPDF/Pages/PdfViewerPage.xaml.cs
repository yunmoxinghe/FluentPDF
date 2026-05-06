using FluentPDF.Backends;
using FluentPDF.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Pages
{
    // ── Stream 对象池已移入 WindowsPdfBackend，Page 层不再需要 ──
    public sealed partial class PdfViewerPage : Page
    {
        private readonly ObservableCollection<PdfPageItem> _pages = new();
        // 渲染后端：默认使用 Windows.Data.Pdf，切换引擎只需换这一行
        private IPdfBackend _backend = new WindowsPdfBackend();

        /// <summary>当前后端名称，供外部判断是否需要切换，避免重复加载。</summary>
        public string CurrentBackendName => _backend.BackendName;

        // ── 渲染档位 ──────────────────────────────────────────────
        private RenderProfile _profile = RenderProfile.Normal;
        public  bool IsLowEndMode => _profile == RenderProfile.LowEnd;

        // ── 渲染任务控制 ───────────────────────────────────────────
        private CancellationTokenSource? _layer2Cts;
        private CancellationTokenSource? _layer1Cts;
        private CancellationTokenSource? _metaCts;      // 元数据加载任务的取消令牌
        private DispatcherTimer?         _debounceTimer;
        // 弱鸡模式单任务锁：保证同一时刻只有一个渲染任务在跑
        private readonly SemaphoreSlim _renderLock = new(1, 1);
        private const int DebounceMs = 80;

        // ── 缓存 ──────────────────────────────────────────────────
        // Layer2 LRU 缓存：容量由档位决定
        private LruCache<(uint, int), BitmapImage> _layer2Cache = new(RenderProfile.Normal.CacheCapacity);
        // Layer1 缩略图缓存：有容量上限，防止大文件把所有缩略图都留在内存
        private LruCache<uint, BitmapImage> _layer1Cache = new(RenderProfile.Normal.Layer1CacheCapacity);

        // ── 固定常量 ──────────────────────────────────────────────
        private const double ZoomStep   = 0.25;
        private const double ZoomMin    = 0.25;
        private const uint   Layer1Width = 160;

        // ── 滚动方向 & 速度追踪 ───────────────────────────────────
        private double _lastVerticalOffset  = 0;
        private long   _lastOffsetTimestamp = 0;  // Stopwatch ticks，高精度
        private double _scrollVelocity      = 0;  // px/ms
        private int    _scrollDirection     = 0;  // +1 向下，-1 向上

        // ── DPI ───────────────────────────────────────────────────
        private double _dpiScale = 1.0;

        // ── CheckAndPreemptForViewport dirty flag ─────────────────
        // Layer2 状态变化时置 true，避免每帧都扫可见页
        private bool _viewportLayer2Dirty = true;

        // ── 上一次 Layer2 渲染的预加载边界 ───────────────────────
        // 只有边界收缩时才需要 evict，避免每帧对全部页面遍历
        private int _lastEvictFrom = -1;
        private int _lastEvictTo   = -1;

        // ── 适合模式切换 ──────────────────────────────────────────
        // false = 适合宽度（默认），true = 适合页面大小
        private bool _fitPageMode = false;
        private StorageFile? _pendingFile;
        private StorageFile? _lastFile;   // 记录最后一次成功加载的文件，切换后端时重新加载用

        public PdfViewerPage()
        {
            this.InitializeComponent();
            PagesRepeater.ItemsSource = _pages;
            PdfScrollViewer.ViewChanged += OnViewChanged;
            PagesRepeater.ElementPrepared  += OnElementPrepared;
            PagesRepeater.ElementClearing  += OnElementClearing;

            // 读取屏幕 DPI 缩放比例，用于渲染时对齐物理像素
            var di = Windows.Graphics.Display.DisplayInformation.GetForCurrentView();
            _dpiScale = di.RawPixelsPerViewPixel;
            di.DpiChanged += (s, _) => _dpiScale = s.RawPixelsPerViewPixel;
        }

        // ── 公开接口 ──────────────────────────────────────────────

        /// <summary>手动切换渲染档位，切换后重新渲染当前视口。</summary>
        public void SetProfile(RenderProfile profile)
        {
            if (_profile == profile) return;
            _profile = profile;
            // 重建缓存（容量变了）
            _layer2Cache = new LruCache<(uint, int), BitmapImage>(profile.CacheCapacity);
            _layer1Cache = new LruCache<uint, BitmapImage>(profile.Layer1CacheCapacity);
            // 更新 ScrollViewer 最大缩放
            PdfScrollViewer.MaxZoomFactor = (float)profile.MaxZoom;
            // 如果当前缩放超出新上限，夹回去
            if (PdfScrollViewer.ZoomFactor > (float)profile.MaxZoom)
                PdfScrollViewer.ChangeView(null, null, (float)profile.MaxZoom, disableAnimation: true);
            ScheduleLayer2();
        }

        /// <summary>
        /// 切换渲染后端。切换后清空缓存并用新后端重新加载当前文件。
        /// 如果当前没有打开文件则只替换后端。
        /// </summary>
        public void SetBackend(IPdfBackend newBackend)
        {
            var old = _backend;
            _backend = newBackend;
            old.Dispose();

            // 有文件时用新后端重新加载
            if (_lastFile != null)
                LoadFile(_lastFile);
        }

        /// <summary>应用学校模式：将工具栏移至底部或恢复顶部。</summary>
        public void ApplySchoolMode(bool enabled)
        {
            if (enabled)
            {
                // 工具栏移到 Row=2，分割线改为顶部对齐
                Grid.SetRow(Toolbar, 2);
                Grid.SetRow(ToolbarDivider, 2);
                ToolbarDivider.VerticalAlignment = Windows.UI.Xaml.VerticalAlignment.Top;
                TopToolbarRow.Height    = new GridLength(0);
                BottomToolbarRow.Height = GridLength.Auto;
            }
            else
            {
                // 工具栏恢复 Row=0，分割线恢复底部对齐
                Grid.SetRow(Toolbar, 0);
                Grid.SetRow(ToolbarDivider, 0);
                ToolbarDivider.VerticalAlignment = Windows.UI.Xaml.VerticalAlignment.Bottom;
                TopToolbarRow.Height    = GridLength.Auto;
                BottomToolbarRow.Height = new GridLength(0);
            }
        }

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
            _pageTopCache = null;   // 清除页顶坐标缓存，防止旧数据被新文档复用

            try
            {
                await _backend.LoadAsync(file);
                uint pageCount = _backend.PageCount;

                // ── 自动档位检测：页数 > 100 或首页超大，自动进入弱鸡模式 ──
                var (p0w, p0h) = _backend.GetPageSize(0);
                // 取整到整数逻辑像素，避免 Image 控件在亚像素尺寸上触发双线性插值模糊
                p0w = Math.Round(p0w);
                p0h = Math.Round(p0h);
                bool autoLowEnd = pageCount > 100 || p0w > 2000 || p0h > 2000;
                _profile = autoLowEnd ? RenderProfile.LowEnd : RenderProfile.Normal;
                _layer2Cache = new LruCache<(uint, int), BitmapImage>(_profile.CacheCapacity);
                _layer1Cache = new LruCache<uint, BitmapImage>(_profile.Layer1CacheCapacity);

                double viewportWidth = PdfScrollViewer.ActualWidth > 0
                    ? PdfScrollViewer.ActualWidth : ActualWidth;

                float initialZoom = 1.0f;
                if (viewportWidth > 0 && p0w > 0)
                    initialZoom = (float)Math.Clamp(viewportWidth / p0w, ZoomMin, _profile.MaxZoom);

                _pages.Add(new PdfPageItem
                {
                    PageIndex     = 0,
                    DisplayWidth  = p0w,
                    DisplayHeight = p0h,
                });

                LoadingRing.IsActive       = false;
                _lastFile                  = file;   // 记录成功加载的文件，切换后端时重新加载用
                PdfScrollViewer.Visibility = Visibility.Visible;
                Toolbar.Visibility         = Visibility.Visible;
                PdfScrollViewer.MaxZoomFactor = (float)_profile.MaxZoom;
                LowEndToggle.IsChecked     = IsLowEndMode;
                ApplySchoolMode(SettingsManager.Instance.SchoolMode);
                PdfScrollViewer.ChangeView(null, null, initialZoom, disableAnimation: true);
                UpdatePageIndicator();

                // 等布局走完一帧再渲染，此时 ViewportHeight 已就绪，GetVisibleRange 能拿到正确范围
                // 避免 Visibility 刚变 Visible 时 ViewportHeight=0 导致 Layer2 直接跳过、出现一帧模糊
                void OnFirstLayout(object? s, object _)
                {
                    PdfScrollViewer.LayoutUpdated -= OnFirstLayout;
                    ScheduleLayer2();
                }
                PdfScrollViewer.LayoutUpdated += OnFirstLayout;

                // 后台补全剩余页元数据，不 await——让 Layer1 立即启动，
                // 元数据任务自己在完成后调 ResumeBackgroundLayer1 补一次。
                if (pageCount > 1)
                {
                    _metaCts = new CancellationTokenSource();
                    _ = LoadRemainingMetaAsync(pageCount, _metaCts.Token);
                }

                // 立即启动后台 Layer1 缩略图渲染（不等元数据全部到位）
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

        // 串行读取剩余页的尺寸元数据。
        // WindowsPdfBackend.GetPageSize() 内部调用 WinRT GetPage()，必须在 UI 线程执行。
        // 整个循环体（含 GetPageSize）都在 RunAsync 里运行，每批让出一次 UI 线程。
        private async Task LoadRemainingMetaAsync(uint pageCount, CancellationToken token)
        {
            const int batchSize = 20;

            for (uint batchStart = 1; batchStart < pageCount; batchStart += batchSize)
            {
                if (token.IsCancellationRequested) return;

                uint batchEnd = Math.Min(batchStart + (uint)batchSize, pageCount);

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                {
                    if (token.IsCancellationRequested) return;
                    for (uint i = batchStart; i < batchEnd; i++)
                    {
                        // GetPageSize 调用 WinRT GetPage()，必须在 UI 线程
                        var (w, h) = _backend.GetPageSize(i);
                        // 取整，保证 Grid 尺寸是整数逻辑像素
                        _pages.Add(new PdfPageItem
                        {
                            PageIndex     = i,
                            DisplayWidth  = Math.Round(w),
                            DisplayHeight = Math.Round(h),
                        });
                    }
                });
            }

            // 元数据全部到位后，补一次 Layer1 确保新增页也被覆盖
            if (!token.IsCancellationRequested)
                ResumeBackgroundLayer1();
        }

        // ── 后台 Layer1 渲染 ──────────────────────────────────────

        // 追踪 Layer1 后台任务，用于判断是否仍在运行
        private Task? _layer1Task;

        private void StartBackgroundLayer1()
        {
            _layer1Cts = new CancellationTokenSource();
            _layer1Task = RenderLayer1Async(_layer1Cts.Token);
        }

        /// <summary>
        /// Layer1：逐页渲染单页缩略图（Layer1Width px 宽）。
        /// 可见区域的页优先渲染，其余按顺序。
        /// </summary>
        private async Task RenderLayer1Async(CancellationToken token)
        {
            if (_backend.PageCount == 0) return;

            var order = BuildLayer1Order();

            foreach (uint i in order)
            {
                if (token.IsCancellationRequested) return;
                if (_layer1Cache.TryGet(i, out _)) continue;
                // 已有 Layer2 则跳过
                if (i < (uint)_pages.Count && _pages[(int)i].IsLayer2Ready) continue;

                try
                {
                    var (pw, ph) = _backend.GetPageSize(i);
                    double aspect = ph / pw;
                    uint w = Layer1Width;
                    uint h = (uint)Math.Max(1, Math.Round(w * aspect));

                    var bitmap = await _backend.RenderPageAsync(i, w, h, token);
                    if (token.IsCancellationRequested) return;
                    if (bitmap == null) continue;

                    _layer1Cache.Put(i, bitmap);
                    if (i < (uint)_pages.Count)
                        _pages[(int)i].SetLayer1(bitmap);
                }
                catch (OperationCanceledException) { return; }
                catch { }

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => { });
            }
        }

        private List<uint> BuildLayer1Order()
        {
            var (first, last) = GetVisibleRange();
            var order = new List<uint>((int)_backend.PageCount);
            // 可见页先加
            for (uint i = (uint)Math.Max(0, first); i <= (uint)Math.Min(_pages.Count - 1, last); i++)
                order.Add(i);
            // 其余页按页码顺序
            for (uint i = 0; i < _backend.PageCount; i++)
                if (i < (uint)Math.Max(0, first) || i > (uint)Math.Min(_pages.Count - 1, last))
                    order.Add(i);
            return order;
        }

        // ── ItemsRepeater 元素生命周期 ────────────────────────────
        // ElementPrepared：ItemsRepeater 把 DataTemplate 实例化（或从回收池取出）后调用，
        // 此时把 PdfPageView 与对应的 PdfPageItem 关联，让数据模型能直接驱动视图动画。
        private void OnElementPrepared(Microsoft.UI.Xaml.Controls.ItemsRepeater sender,
            Microsoft.UI.Xaml.Controls.ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is PdfPageView view && args.Index < _pages.Count)
                _pages[args.Index].AttachView(view);
        }

        // ElementClearing：元素被回收前调用，解除关联，避免悬空引用。
        private void OnElementClearing(Microsoft.UI.Xaml.Controls.ItemsRepeater sender,
            Microsoft.UI.Xaml.Controls.ItemsRepeaterElementClearingEventArgs args)
        {
            if (args.Element is PdfPageView view)
            {
                foreach (var item in _pages)
                {
                    if (item.IsAttachedTo(view)) { item.DetachView(); break; }
                }
            }
        }

        // ── 缩放控件 ──────────────────────────────────────────────

        private void LowEndToggle_Click(object sender, RoutedEventArgs e)
        {
            bool goLowEnd = LowEndToggle.IsChecked == true;
            SetProfile(goLowEnd ? RenderProfile.LowEnd : RenderProfile.Normal);
        }

        // ── 页数跳转 ──────────────────────────────────────────────

        /// <summary>更新页码显示框和总页数文本（1-based）。</summary>
        private void UpdatePageIndicator()
        {
            if (_pages.Count == 0) return;
            var (first, _) = GetVisibleRange();
            int current = Math.Max(0, first);
            PageNumberBox.Text  = (current + 1).ToString();
            TotalPagesText.Text = $"/ {_pages.Count}";
        }

        /// <summary>跳转到指定页（0-based）。</summary>
        private void JumpToPage(int pageIndex)
        {
            if (_pages.Count == 0) return;
            pageIndex = Math.Clamp(pageIndex, 0, _pages.Count - 1);

            var tops = GetPageTops();
            double pageTop = tops[pageIndex] * PdfScrollViewer.ZoomFactor;

            // 水平居中
            double extentW = PdfScrollViewer.ExtentWidth;
            double viewW   = PdfScrollViewer.ViewportWidth;
            double centerH = Math.Max(0, (extentW - viewW) / 2);

            PdfScrollViewer.ChangeView(centerH, pageTop, null, disableAnimation: false);
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            var (first, _) = GetVisibleRange();
            JumpToPage(Math.Max(0, first - 1));
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            var (_, last) = GetVisibleRange();
            JumpToPage(Math.Min(_pages.Count - 1, last + 1));
        }

        private void PageNumberBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitPageNumberBox();
                e.Handled = true;
            }
        }

        private void PageNumberBox_LostFocus(object sender, RoutedEventArgs e)
            => CommitPageNumberBox();

        private void CommitPageNumberBox()
        {
            if (int.TryParse(PageNumberBox.Text, out int page))
                JumpToPage(page - 1);   // 用户输入 1-based，内部 0-based
            else
                UpdatePageIndicator();  // 输入非法时恢复原值
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            float cur = PdfScrollViewer.ZoomFactor;
            float next = (float)Math.Min(Math.Round(cur / ZoomStep + 1) * ZoomStep, _profile.MaxZoom);
            ZoomAroundViewportCenter(next);
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            float cur = PdfScrollViewer.ZoomFactor;
            float next = (float)Math.Max(Math.Round(cur / ZoomStep - 1) * ZoomStep, ZoomMin);
            ZoomAroundViewportCenter(next);
        }

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pages.Count == 0) return;

            // 每次点击切换模式
            _fitPageMode = !_fitPageMode;

            if (_fitPageMode)
            {
                // ── 当前：适合页面大小，点击后将切换回适合宽度 ──
                // 图标和文字显示"下次点击的效果"即适合宽度
                ZoomFitIcon.Glyph       = "\uEAD6";

                // 执行：适合页面大小（完整显示当前页）
                var (firstVisible, _) = GetVisibleRange();
                int targetPage = Math.Max(0, firstVisible);
                var page = _pages[targetPage];

                double vw = PdfScrollViewer.ViewportWidth  > 0 ? PdfScrollViewer.ViewportWidth  : PdfScrollViewer.ActualWidth;
                double vh = PdfScrollViewer.ViewportHeight > 0 ? PdfScrollViewer.ViewportHeight : PdfScrollViewer.ActualHeight;

                float fitW = (float)(vw / page.DisplayWidth);
                float fitH = (float)(vh / page.DisplayHeight);
                float fit  = (float)Math.Clamp(Math.Min(fitW, fitH), ZoomMin, _profile.MaxZoom);

                double extentAfter = PdfScrollViewer.ExtentWidth * (fit / PdfScrollViewer.ZoomFactor);
                double centerH     = Math.Max(0, (extentAfter - vw) / 2);
                double pageTopV    = GetPageTops()[targetPage] * fit;

                PdfScrollViewer.ChangeView(centerH, pageTopV, fit, disableAnimation: false);
            }
            else
            {
                // ── 当前：适合宽度，点击后将切换回适合页面大小 ──
                // 图标和文字显示"下次点击的效果"即适合页面大小
                ZoomFitIcon.Glyph       = "\uE9A6";

                // 执行：适合宽度
                double vw = PdfScrollViewer.ViewportWidth > 0
                    ? PdfScrollViewer.ViewportWidth
                    : PdfScrollViewer.ActualWidth;
                float fit = (float)Math.Clamp(
                    vw / _pages[0].DisplayWidth, ZoomMin, _profile.MaxZoom);

                double ratio       = fit / PdfScrollViewer.ZoomFactor;
                double extentAfter = PdfScrollViewer.ExtentWidth * ratio;
                double centerH     = Math.Max(0, (extentAfter - PdfScrollViewer.ViewportWidth) / 2);
                double centerV     = (PdfScrollViewer.VerticalOffset + PdfScrollViewer.ViewportHeight / 2)
                                     * ratio - PdfScrollViewer.ViewportHeight / 2;
                centerV = Math.Max(0, centerV);

                PdfScrollViewer.ChangeView(centerH, centerV, fit, disableAnimation: false);
            }
        }

        /// <summary>
        /// 以视口中心为基准点缩放，并带平滑动画。
        /// 计算原理：缩放后保持视口中心对应的内容坐标不变，
        /// 即 newOffset = (oldOffset + viewportSize/2) * (newZoom/oldZoom) - viewportSize/2
        /// </summary>
        private void ZoomAroundViewportCenter(float newZoom)
        {
            float oldZoom = PdfScrollViewer.ZoomFactor;
            double ratio  = newZoom / oldZoom;

            double newH = (PdfScrollViewer.HorizontalOffset + PdfScrollViewer.ViewportWidth  / 2) * ratio
                          - PdfScrollViewer.ViewportWidth  / 2;
            double newV = (PdfScrollViewer.VerticalOffset   + PdfScrollViewer.ViewportHeight / 2) * ratio
                          - PdfScrollViewer.ViewportHeight / 2;

            PdfScrollViewer.ChangeView(newH, newV, newZoom, disableAnimation: false);
        }

        private void PdfScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 只设 MinWidth 保证内容居中，不固定 Width。
            // 固定 Width 会在缩放时裁切内容：ScrollViewer 对内容整体缩放，
            // 若 PagesHost.Width 被钉死为视口宽，缩放后实际像素宽度不变，
            // 导致超出视口的部分被裁掉或水平滚动条无法正确出现。
            double vw = PdfScrollViewer.ViewportWidth;
            double w  = vw > 0 ? vw : PdfScrollViewer.ActualWidth;
            PagesHost.MinWidth = w;
            // 只在需要时清除固定宽度，避免每次 SizeChanged 都触发额外布局重算
            if (!double.IsNaN(PagesHost.Width))
                PagesHost.Width = double.NaN; // Auto
        }

        // ── 视图变化调度 ──────────────────────────────────────────

        private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            // 计算滚动速度（px/ms）和方向
            // 使用 Stopwatch 高精度时间戳，避免 TickCount64 在高帧率下精度不足
            double currentOffset = PdfScrollViewer.VerticalOffset;
            long   nowTicks      = Stopwatch.GetTimestamp();
            double dt            = (nowTicks - _lastOffsetTimestamp)
                                   * 1000.0 / Stopwatch.Frequency; // 转换为毫秒
            if (dt > 0 && dt < 200) // 忽略过长间隔（暂停后第一帧）
                _scrollVelocity = (currentOffset - _lastVerticalOffset) / dt;
            else
                _scrollVelocity = 0;

            if (currentOffset > _lastVerticalOffset)      _scrollDirection = 1;
            else if (currentOffset < _lastVerticalOffset) _scrollDirection = -1;
            _lastVerticalOffset  = currentOffset;
            _lastOffsetTimestamp = nowTicks;

            CheckAndPreemptForViewport();
            UpdatePageIndicator();

            if (!e.IsIntermediate)
            {
                _debounceTimer?.Stop();
                if (_debounceTimer == null)
                {
                    _debounceTimer = new DispatcherTimer
                        { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
                    _debounceTimer.Tick += (s, _) =>
                    {
                        _debounceTimer!.Stop();
                        ScheduleLayer2();
                    };
                }
                _debounceTimer.Start();
            }
        }

        private void CheckAndPreemptForViewport()
        {
            if (_backend.PageCount == 0 || _pages.Count == 0) return;
            // dirty flag：只有可见页 Layer2 状态可能变化时才扫描，避免每帧 O(n) 遍历
            if (!_viewportLayer2Dirty) return;

            var (first, last) = GetVisibleRange();
            if (first < 0) return;

            bool needsLayer2 = false;
            for (int i = first; i <= last; i++)
            {
                if (i >= _pages.Count) break;
                if (!_pages[i].IsLayer2Ready) { needsLayer2 = true; break; }
            }

            if (!needsLayer2)
            {
                // 可见页全部就绪，清除 dirty，后续帧跳过扫描
                _viewportLayer2Dirty = false;
                return;
            }

            // 只取消 Layer1，让出渲染资源给即将到来的 Layer2
            _layer1Cts?.Cancel();
            _layer1Cts = null;
        }

        private void ScheduleLayer2()
        {
            // 新一轮渲染开始，可见页 Layer2 状态可能变化，重置 dirty
            _viewportLayer2Dirty = true;
            _layer2Cts?.Cancel();
            _layer2Cts = new CancellationTokenSource();
            _ = RenderLayer2Async(_layer2Cts.Token);
        }

        // ── Layer2 高分辨率渲染 ───────────────────────────────────

        private async Task RenderLayer2Async(CancellationToken token)
        {
            if (_backend.PageCount == 0 || _pages.Count == 0) return;

            var p    = _profile; // 快照，避免渲染中途档位切换导致不一致
            float zoom = PdfScrollViewer.ZoomFactor;
            var (firstVisible, lastVisible) = GetVisibleRange();
            if (firstVisible < 0) return;

            // ── 动态分辨率 ────────────────────────────────────────
            double absVel          = Math.Abs(_scrollVelocity);
            double resolutionScale = absVel < 0.5 ? p.ResolutionScaleSlow
                                   : absVel < 2.0 ? (p.ResolutionScaleSlow + p.ResolutionScaleFast) / 2.0
                                   :                p.ResolutionScaleFast;
            double renderScale = zoom * resolutionScale * _dpiScale;

            // ── 预测停止点 ────────────────────────────────────────
            double predictedOffset     = PdfScrollViewer.VerticalOffset + _scrollVelocity * 150;
            double predictedViewTop    = predictedOffset / zoom;
            double predictedViewBottom = predictedViewTop + PdfScrollViewer.ViewportHeight / zoom;

            int predFirst = firstVisible, predLast = lastVisible;
            {
                var tops = GetPageTops();
                for (int i = 0; i < _pages.Count; i++)
                {
                    double bottom = tops[i] + _pages[i].DisplayHeight;
                    if (bottom >= predictedViewTop && tops[i] <= predictedViewBottom)
                    { predFirst = Math.Min(i, predFirst); predLast = Math.Max(i, predLast); }
                }
            }

            // ── 预加载范围 ────────────────────────────────────────
            int ahead  = _scrollDirection >= 0 ? p.PreloadAhead  : p.PreloadBehind;
            int behind = _scrollDirection >= 0 ? p.PreloadBehind : p.PreloadAhead;
            int from   = Math.Max(0, Math.Min(firstVisible, predFirst) - behind);
            int to     = Math.Min(_pages.Count - 1, Math.Max(lastVisible, predLast) + ahead);

            int center     = (firstVisible + lastVisible) / 2;
            var order      = BuildRenderOrder(center, from, to);
            int zoomBucket = ZoomToBucket((float)renderScale);

            // ── 按需 Evict：只在预加载窗口收缩时才遍历 ──────────
            // 窗口扩大或不变时跳过，避免每次渲染都对全部页面 O(n) 扫描。
            if (_lastEvictFrom >= 0)
            {
                // 左侧新增的窗口外区间：[_lastEvictFrom, from-1]
                for (int i = _lastEvictFrom; i < from && i < _pages.Count; i++)
                    _pages[i].EvictLayer2(_layer1Cache);
                // 右侧新增的窗口外区间：[to+1, _lastEvictTo]
                for (int i = to + 1; i <= _lastEvictTo && i < _pages.Count; i++)
                    _pages[i].EvictLayer2(_layer1Cache);
            }
            _lastEvictFrom = from;
            _lastEvictTo   = to;

            // ── 第一轮：可见区，批量渲染 + 合帧 ─────────────────
            var visibleOrder = new List<int>();
            foreach (int i in order)
                if (i >= firstVisible && i <= lastVisible) visibleOrder.Add(i);

            for (int b = 0; b < visibleOrder.Count; b += p.BatchSize)
            {
                if (token.IsCancellationRequested) return;

                int batchEnd   = Math.Min(b + p.BatchSize, visibleOrder.Count);
                var batchItems = new List<(PdfPageItem item, int idx)>();

                for (int k = b; k < batchEnd; k++)
                {
                    int i    = visibleOrder[k];
                    var item = _pages[i];
                    if (_layer2Cache.TryGet((item.PageIndex, zoomBucket), out var cached))
                        item.SetLayer2(cached!);
                    else
                        batchItems.Add((item, i));
                }

                if (batchItems.Count == 0) continue;

                Task<BitmapImage?>[] tasks;
                if (p.BatchSize == 1)
                {
                    // 弱鸡模式：单任务，走渲染锁
                    tasks = new[] { RenderWithLockAsync(batchItems[0].item, (float)renderScale, p, token) };
                }
                else
                {
                    tasks = batchItems.ConvertAll(x =>
                        RenderLayer2PageToBitmapAsync(x.item, (float)renderScale, p, token)).ToArray();
                }

                var results = await Task.WhenAll(tasks);
                if (token.IsCancellationRequested) return;

                for (int k = 0; k < batchItems.Count; k++)
                {
                    if (results[k] is not { } bitmap) continue;
                    var (item, _) = batchItems[k];
                    _layer2Cache.Put((item.PageIndex, zoomBucket), bitmap);
                    item.SetLayer2(bitmap);
                }
            }

            if (token.IsCancellationRequested) return;

            // ── 第二轮：预加载范围，串行，不写缓存 ───────────────
            foreach (int i in order)
            {
                if (token.IsCancellationRequested) return;
                var item = _pages[i];
                if (item.IsLayer2Ready) continue;
                if (i >= firstVisible && i <= lastVisible) continue;

                if (_layer2Cache.TryGet((item.PageIndex, zoomBucket), out var cached))
                { item.SetLayer2(cached!); continue; }

                // 弱鸡模式：补高清时每页之间加 60ms 间隔，避免一次性补帧风暴
                if (p.BatchSize == 1) await Task.Delay(60, token);
                if (token.IsCancellationRequested) return;

                var bmp = await RenderWithLockAsync(item, (float)renderScale, p, token);
                if (bmp != null && !token.IsCancellationRequested)
                    item.SetLayer2(bmp);
            }

            if (!token.IsCancellationRequested)
                ResumeBackgroundLayer1();
        }

        /// <summary>弱鸡模式专用：通过 SemaphoreSlim 保证单任务渲染。</summary>
        private async Task<BitmapImage?> RenderWithLockAsync(
            PdfPageItem item, float renderScale, RenderProfile p, CancellationToken token)
        {
            await _renderLock.WaitAsync(token);
            try   { return await RenderLayer2PageToBitmapAsync(item, renderScale, p, token); }
            finally { _renderLock.Release(); }
        }

        /// <summary>只渲染，不更新 UI，不写缓存。由调用方决定何时合帧。</summary>
        private async Task<BitmapImage?> RenderLayer2PageToBitmapAsync(
            PdfPageItem item, float renderScale, RenderProfile p, CancellationToken token)
        {
            try
            {
                uint w = AlignTo4((uint)Math.Max(1, Math.Round(item.DisplayWidth  * renderScale)));
                uint h = AlignTo4((uint)Math.Max(1, Math.Round(item.DisplayHeight * renderScale)));

                if (w > p.MaxRenderDim || h > p.MaxRenderDim)
                {
                    double scale = Math.Min((double)p.MaxRenderDim / w, (double)p.MaxRenderDim / h);
                    w = AlignTo4((uint)Math.Max(1, Math.Round(w * scale)));
                    h = AlignTo4((uint)Math.Max(1, Math.Round(h * scale)));
                }

                return await _backend.RenderPageAsync(item.PageIndex, w, h, token);
            }
            catch (OperationCanceledException) { return null; }
            catch { return null; }
        }

        /// <summary>宽度/高度对齐到 4 的倍数，改善 GPU 纹理对齐效率。</summary>
        private static uint AlignTo4(uint v) => (v + 3u) & ~3u;

        private void ResumeBackgroundLayer1()
        {
            // 任务仍在运行时不重复启动（用 Task 状态判断，比检查 CTS 是否为 null 更可靠）
            if (_layer1Task != null && !_layer1Task.IsCompleted) return;

            bool needsLayer1 = false;
            for (int i = 0; i < _pages.Count; i++)
                if (!_pages[i].HasLayer1) { needsLayer1 = true; break; }

            if (needsLayer1)
                StartBackgroundLayer1();
        }

        // ── 工具方法 ──────────────────────────────────────────────

        // ── 页面 Y 坐标索引（用于二分查找） ──────────────────────
        // 每次 _pages 变化后按需重建，GetVisibleRange 用它做 O(log n) 查找。
        private double[]? _pageTopCache;   // 每页顶部 Y（未缩放内容坐标）
        private int       _pageTopCacheVersion = -1;  // 用 int 与 _pages.Count 类型一致

        private double[] GetPageTops()
        {
            // 用 _pages.Count 作为版本号：页数不变时复用缓存
            if (_pageTopCache != null && _pageTopCacheVersion == _pages.Count)
                return _pageTopCache;

            const double spacing = 16.0;
            var tops = new double[_pages.Count];
            double y = 0;
            for (int i = 0; i < _pages.Count; i++)
            {
                tops[i] = y;
                y += _pages[i].DisplayHeight + spacing;
            }
            _pageTopCache        = tops;
            _pageTopCacheVersion = _pages.Count;
            return tops;
        }

        private (int first, int last) GetVisibleRange()
        {
            if (_pages.Count == 0) return (-1, -1);
            float  zoom       = PdfScrollViewer.ZoomFactor;
            double viewTop    = PdfScrollViewer.VerticalOffset / zoom;
            double viewBottom = viewTop + PdfScrollViewer.ViewportHeight / zoom;

            var tops = GetPageTops();

            // 二分找第一个 bottom >= viewTop 的页（即 top + height >= viewTop）
            int lo = 0, hi = _pages.Count - 1, first = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                double bottom = tops[mid] + _pages[mid].DisplayHeight;
                if (bottom >= viewTop) { first = mid; hi = mid - 1; }
                else lo = mid + 1;
            }
            if (first < 0) return (-1, -1);

            // 从 first 向后线性扫到 top > viewBottom（可见页通常只有几页，线性足够）
            int last = first;
            for (int i = first + 1; i < _pages.Count; i++)
            {
                if (tops[i] > viewBottom) break;
                last = i;
            }
            return (first, last);
        }

        // bucket 粒度 0.25：基于已乘 DPI 的 renderScale，
        // 缩放差 < 0.125 时复用同一缓存，高 DPI 屏命中率更高
        private static int ZoomToBucket(float renderScale)
            => (int)Math.Round(renderScale / 0.25f);

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
            _layer1Cts?.Cancel(); _layer1Cts = null; _layer1Task = null;
            _metaCts?.Cancel();   _metaCts   = null;
            // 重置 evict 边界，防止旧文档边界污染新文档的首次渲染
            _lastEvictFrom = -1;
            _lastEvictTo   = -1;
        }

        public bool TryGetThumb(uint pageIndex, out BitmapImage? thumb)
            => _layer1Cache.TryGet(pageIndex, out thumb);
    }
}
