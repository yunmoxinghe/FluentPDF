using System.Numerics;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace FluentPDF.Annotations;

/// <summary>
/// 工具配置静态类，定义各工具的预设颜色和粗细
/// </summary>
public static class ToolConfiguration
{
    /// <summary>
    /// 荧光笔预设颜色（黄色、绿色、蓝色、粉色、橙色、紫色）
    /// </summary>
    public static readonly Color[] HighlighterColors = new[]
    {
        Color.FromArgb(128, 255, 255, 0),   // 黄色，50% 透明度
        Color.FromArgb(128, 0, 255, 0),     // 绿色，50% 透明度
        Color.FromArgb(128, 0, 191, 255),   // 蓝色，50% 透明度
        Color.FromArgb(128, 255, 105, 180), // 粉色，50% 透明度
        Color.FromArgb(128, 255, 165, 0),   // 橙色，50% 透明度
        Color.FromArgb(128, 138, 43, 226)   // 紫色，50% 透明度
    };

    /// <summary>
    /// 荧光笔预设粗细（细、中、粗）
    /// </summary>
    public static readonly double[] HighlighterSizes = new[] { 8.0, 16.0, 24.0 };

    /// <summary>
    /// 硬笔预设颜色（黑色、红色、蓝色、绿色、橙色、紫色、棕色、灰色、白色）
    /// </summary>
    public static readonly Color[] PenColors = new[]
    {
        Colors.Black,
        Colors.Red,
        Colors.Blue,
        Colors.Green,
        Color.FromArgb(255, 255, 140, 0),   // 深橙色
        Color.FromArgb(255, 138, 43, 226),  // 紫色
        Color.FromArgb(255, 139, 69, 19),   // 棕色
        Colors.Gray,
        Colors.White
    };

    /// <summary>
    /// 硬笔预设粗细（细、中、粗）
    /// </summary>
    public static readonly double[] PenSizes = new[] { 1.0, 2.0, 4.0 };

    /// <summary>
    /// 橡皮擦预设尺寸（小、大）
    /// </summary>
    public static readonly double[] EraserSizes = new[] { 16.0, 32.0 };
}

/// <summary>
/// 自定义荧光笔
/// </summary>
public class CustomHighlighterPen : InkToolbarCustomPen
{
    protected override InkDrawingAttributes CreateInkDrawingAttributesCore(Brush brush, double strokeWidth)
    {
        var inkDrawingAttributes = new InkDrawingAttributes
        {
            PenTip = PenTipShape.Rectangle,
            Size = new Windows.Foundation.Size(strokeWidth, strokeWidth / 2),
            IgnorePressure = true,
            FitToCurve = true
        };

        if (brush is SolidColorBrush solidColorBrush)
        {
            inkDrawingAttributes.Color = solidColorBrush.Color;
        }
        else
        {
            inkDrawingAttributes.Color = Color.FromArgb(128, 255, 255, 0); // 默认黄色
        }

        return inkDrawingAttributes;
    }
}

/// <summary>
/// 自定义硬笔
/// </summary>
public class CustomBallpointPen : InkToolbarCustomPen
{
    protected override InkDrawingAttributes CreateInkDrawingAttributesCore(Brush brush, double strokeWidth)
    {
        var inkDrawingAttributes = new InkDrawingAttributes
        {
            PenTip = PenTipShape.Circle,
            Size = new Windows.Foundation.Size(strokeWidth, strokeWidth),
            IgnorePressure = false,
            FitToCurve = true
        };

        if (brush is SolidColorBrush solidColorBrush)
        {
            inkDrawingAttributes.Color = solidColorBrush.Color;
        }
        else
        {
            inkDrawingAttributes.Color = Colors.Black; // 默认黑色
        }

        return inkDrawingAttributes;
    }
}
