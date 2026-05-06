using FluentPDF.Helpers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Pages
{
    /// <summary>
    /// 单页数据模型。持有双层双缓冲图像，并通过弱引用驱动关联的 <see cref="PdfPageView"/>。
    /// 所有方法必须在 UI 线程调用。
    /// </summary>
    public sealed class PdfPageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void Notify([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public uint PageIndex { get; set; }

        // ── 原始尺寸（不随旋转变化，用于渲染时计算 bitmap 尺寸） ──
        public double OriginalWidth  { get; set; }
        public double OriginalHeight { get; set; }

        // ── 显示尺寸（旋转 90°/270° 时宽高对调，驱动 ItemsRepeater 布局） ──
        private double _displayWidth;
        private double _displayHeight;

        public double DisplayWidth
        {
            get => _displayWidth;
            set { if (_displayWidth != value) { _displayWidth = value; Notify(); } }
        }

        public double DisplayHeight
        {
            get => _displayHeight;
            set { if (_displayHeight != value) { _displayHeight = value; Notify(); } }
        }

        // ── 旋转角度（0 / 90 / 180 / 270） ───────────────────────
        private int _rotationDegrees;
        public int RotationDegrees
        {
            get => _rotationDegrees;
            set { if (_rotationDegrees != value) { _rotationDegrees = value; Notify(); } }
        }

        // ── 双层各自的双缓冲（纯数据，不再驱动 Opacity） ─────────
        private BitmapImage? _l1Back,  _l1Front;
        private BitmapImage? _l2Back,  _l2Front;

        // ── 关联的视图控件（由 ItemsRepeater.ElementPrepared 注入） ──
        // 持有弱引用，避免阻止 UI 元素被 ItemsRepeater 回收
        private System.WeakReference<PdfPageView>? _viewRef;

        internal void AttachView(PdfPageView view)
        {
            _viewRef = new System.WeakReference<PdfPageView>(view);
            // 把当前已有的图像同步到新视图（例如 ItemsRepeater 回收再复用时）
            if (_viewRef.TryGetTarget(out var v))
            {
                v.SetLayer1(_l1Back, _l1Front);
                v.SetLayer2(_l2Back, _l2Front);
                v.SetRotation(_rotationDegrees, OriginalWidth, OriginalHeight);
            }
        }

        internal void DetachView() => _viewRef = null;

        internal bool IsAttachedTo(PdfPageView view)
            => _viewRef != null && _viewRef.TryGetTarget(out var v) && ReferenceEquals(v, view);

        public bool HasLayer1     => _l1Front != null;
        public bool IsLayer2Ready => _l2Front != null;

        public void SetLayer1(BitmapImage bmp)
        {
            _l1Back  = null;
            _l1Front = bmp;
            if (_viewRef?.TryGetTarget(out var v) == true)
                v.SetLayer1(null, bmp);
        }

        public void SetLayer2(BitmapImage bmp)
        {
            _l2Back  = _l2Front;   // 旧图成为 back，保持显示直到淡入完成
            _l2Front = bmp;
            if (_viewRef?.TryGetTarget(out var v) == true)
                v.SetLayer2(_l2Back, bmp);  // 动画在 PdfPageView 里由 Composition 驱动
        }

        /// <summary>应用旋转：对调显示宽高，并通知视图更新 RenderTransform。</summary>
        internal void ApplyRotation(int degrees)
        {
            RotationDegrees = degrees;
            bool sideways = degrees == 90 || degrees == 270;
            DisplayWidth  = sideways ? OriginalHeight : OriginalWidth;
            DisplayHeight = sideways ? OriginalWidth  : OriginalHeight;

            if (_viewRef?.TryGetTarget(out var v) == true)
                v.SetRotation(degrees, OriginalWidth, OriginalHeight);
        }

        /// <summary>
        /// 释放 Layer2，回退到下方的 Layer1 显示。
        /// 仅在已有 Layer1 兜底时才释放，避免出现空白页。
        /// </summary>
        internal void EvictLayer2(LruCache<uint, BitmapImage> layer1Cache)
        {
            if (_l2Front == null) return;
            if (HasLayer1 || layer1Cache.TryGet(PageIndex, out _))
            {
                _l2Front = null;
                _l2Back  = null;
                if (_viewRef?.TryGetTarget(out var v) == true)
                    v.ClearLayer2();
            }
        }
    }
}
