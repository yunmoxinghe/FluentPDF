using Windows.UI;

namespace FluentPDF.Annotations;

/// <summary>
/// 工具状态类，管理当前工具和各工具的配置
/// </summary>
public sealed class ToolState
{
    /// <summary>
    /// 当前选中的工具
    /// </summary>
    public AnnotationTool CurrentTool { get; set; }

    /// <summary>
    /// 荧光笔颜色
    /// </summary>
    public Color HighlighterColor { get; set; }

    /// <summary>
    /// 荧光笔粗细
    /// </summary>
    public double HighlighterSize { get; set; }

    /// <summary>
    /// 硬笔颜色
    /// </summary>
    public Color PenColor { get; set; }

    /// <summary>
    /// 硬笔粗细
    /// </summary>
    public double PenSize { get; set; }

    /// <summary>
    /// 橡皮擦尺寸
    /// </summary>
    public double EraserSize { get; set; }

    /// <summary>
    /// 构造函数，初始化默认值
    /// </summary>
    public ToolState()
    {
        CurrentTool = AnnotationTool.None;
        
        // 荧光笔默认值：黄色，中等粗细
        HighlighterColor = Color.FromArgb(128, 255, 255, 0);
        HighlighterSize = 16.0;
        
        // 硬笔默认值：黑色，细笔
        PenColor = Colors.Black;
        PenSize = 2.0;
        
        // 橡皮擦默认值：小尺寸
        EraserSize = 16.0;
    }
}
