using FluentPDF.Helpers;
using System.Collections.Generic;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Pages
{
    /// <summary>
    /// 单页数据模型。持有双层双缓冲图像，并通过弱引用驱动关联的 <see cref="PdfPageView"/>。
    /// 所有方法必须在 UI 线程调用。
    /// </summary>
    public sealed class PdfPageItem
    {
        public uint   PageIndex     { get; set; }
        public double DisplayWidth  { get; set; }
        public double DisplayHeight { get; set; }

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
