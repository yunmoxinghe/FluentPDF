using System.Numerics;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Pages
{
    /// <summary>
    /// 单页渲染视图。
    /// 持有四个 Image 控件（Layer1/Layer2 各自的 Back/Front 双缓冲），
    /// Layer2Front 的淡入动画通过 Composition ScalarKeyFrameAnimation 驱动，
    /// 运行在 DWM 合成线程，不占 UI 线程。
    /// </summary>
    public sealed partial class PdfPageView : UserControl
    {
        private const float FadeDurationMs = 150f;

        // Composition 对象，Loaded 后初始化
        private Compositor?          _compositor;
        private Visual?              _layer2FrontVisual;
        private ScalarKeyFrameAnimation? _fadeInAnim;

        public PdfPageView()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _compositor        = ElementCompositionPreview.GetElementVisual(Layer2FrontImage).Compositor;
            _layer2FrontVisual = ElementCompositionPreview.GetElementVisual(Layer2FrontImage);

            // 预建动画对象，避免每次 SetLayer2 时重新分配
            _fadeInAnim = _compositor.CreateScalarKeyFrameAnimation();
            _fadeInAnim.InsertKeyFrame(0f, 0f);
            _fadeInAnim.InsertKeyFrame(1f, 1f,
                _compositor.CreateCubicBezierEasingFunction(   // ease-out cubic
                    new Vector2(0f, 0f), new Vector2(0.58f, 1f)));
            _fadeInAnim.Duration = System.TimeSpan.FromMilliseconds(FadeDurationMs);
            _fadeInAnim.Target   = nameof(Visual.Opacity);
        }

        // ── 公开接口（由 PdfPageItem 数据绑定变化时调用） ─────────

        /// <summary>更新 Layer1 图像（缩略图，无动画）。</summary>
        public void SetLayer1(BitmapImage? back, BitmapImage? front)
        {
            Layer1BackImage.Source  = back;
            Layer1FrontImage.Source = front;
        }

        /// <summary>
        /// 更新 Layer2 图像并启动淡入动画。
        /// back 是旧的高清图（保持显示直到淡入完成），front 是新图。
        /// </summary>
        public void SetLayer2(BitmapImage? back, BitmapImage? front)
        {
            Layer2BackImage.Source  = back;
            Layer2FrontImage.Source = front;

            if (front == null || _layer2FrontVisual == null || _fadeInAnim == null)
                return;

            // 立即将 Composition Visual 的 Opacity 设为 0，再启动动画
            // 这样即使 XAML 渲染还没走完，合成层已经是透明的，不会闪一帧
            _layer2FrontVisual.Opacity = 0f;
            _layer2FrontVisual.StartAnimation(nameof(Visual.Opacity), _fadeInAnim);
        }

        /// <summary>清除 Layer2（回退到 Layer1 显示）。</summary>
        public void ClearLayer2()
        {
            _layer2FrontVisual?.StopAnimation(nameof(Visual.Opacity));
            if (_layer2FrontVisual != null)
                _layer2FrontVisual.Opacity = 1f;
            Layer2FrontImage.Source = null;
            Layer2BackImage.Source  = null;
        }
    }
}
