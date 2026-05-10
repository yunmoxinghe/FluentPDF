using System;
using System.Collections.Generic;
using Windows.UI.Input.Inking;

namespace FluentPDF.Annotations;

/// <summary>
/// 标注操作类，用于撤销/重做功能
/// </summary>
public sealed class AnnotationOperation
{
    /// <summary>
    /// 操作类型
    /// </summary>
    public AnnotationOperationType Type { get; }

    /// <summary>
    /// 页面索引
    /// </summary>
    public uint PageIndex { get; }

    /// <summary>
    /// 笔画列表
    /// </summary>
    public IReadOnlyList<InkStroke> Strokes { get; }

    /// <summary>
    /// 操作时间戳
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="type">操作类型</param>
    /// <param name="pageIndex">页面索引</param>
    /// <param name="strokes">笔画列表</param>
    public AnnotationOperation(
        AnnotationOperationType type,
        uint pageIndex,
        IReadOnlyList<InkStroke> strokes)
    {
        Type = type;
        PageIndex = pageIndex;
        Strokes = strokes ?? throw new ArgumentNullException(nameof(strokes));
        Timestamp = DateTime.UtcNow;
    }
}
