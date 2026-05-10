using System.Collections.Generic;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;

namespace FluentPDF.Annotations;

/// <summary>
/// 页面标注数据类，封装单个页面的标注数据和状态
/// </summary>
public sealed class PageAnnotationData
{
    /// <summary>
    /// 页面索引
    /// </summary>
    public uint PageIndex { get; }

    /// <summary>
    /// 墨迹笔画容器
    /// </summary>
    public InkStrokeContainer StrokeContainer { get; }

    /// <summary>
    /// 已修改标记，指示页面标注是否有未保存的更改
    /// </summary>
    public bool IsModified { get; set; }

    /// <summary>
    /// 已加载标记，指示页面标注数据是否已从文件加载
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// 附加的 InkCanvas 控件，用于 UI 绑定
    /// </summary>
    public InkCanvas? AttachedCanvas { get; private set; }

    /// <summary>
    /// 构造函数，初始化页面标注数据
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    public PageAnnotationData(uint pageIndex)
    {
        PageIndex = pageIndex;
        StrokeContainer = new InkStrokeContainer();
        IsModified = false;
        IsLoaded = false;
        AttachedCanvas = null;
    }

    /// <summary>
    /// 将 InkCanvas 与 StrokeContainer 绑定
    /// </summary>
    /// <param name="canvas">要绑定的 InkCanvas 控件</param>
    public void AttachCanvas(InkCanvas canvas)
    {
        AttachedCanvas = canvas;
        canvas.InkPresenter.StrokeContainer = StrokeContainer;
    }

    /// <summary>
    /// 解除 InkCanvas 绑定并清理资源
    /// </summary>
    public void DetachCanvas()
    {
        if (AttachedCanvas != null)
        {
            // 创建新的空容器以清理 InkPresenter 的引用
            AttachedCanvas.InkPresenter.StrokeContainer = new InkStrokeContainer();
            AttachedCanvas = null;
        }
    }

    /// <summary>
    /// 将笔画数据渲染到 InkCanvas
    /// </summary>
    public void ApplyStrokesToCanvas()
    {
        if (AttachedCanvas != null && IsLoaded)
        {
            AttachedCanvas.InkPresenter.StrokeContainer = StrokeContainer;
        }
    }

    /// <summary>
    /// 返回当前页面的所有笔画
    /// </summary>
    /// <returns>笔画集合的只读列表</returns>
    public IReadOnlyList<InkStroke> GetStrokes()
    {
        return StrokeContainer.GetStrokes();
    }
}
