using System;
using System.Collections.Generic;
using Windows.UI.Input.Inking;

namespace FluentPDF.Annotations;

/// <summary>
/// 撤销/重做管理器，管理标注操作历史
/// </summary>
public sealed class UndoRedoManager
{
    private readonly Stack<AnnotationOperation> _undoStack;
    private readonly Stack<AnnotationOperation> _redoStack;
    private const int MaxUndoStackSize = 50;

    /// <summary>
    /// 是否可以撤销
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// 是否可以重做
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// 撤销栈中的操作数量
    /// </summary>
    public int UndoStackCount => _undoStack.Count;

    /// <summary>
    /// 重做栈中的操作数量
    /// </summary>
    public int RedoStackCount => _redoStack.Count;

    /// <summary>
    /// 状态改变事件，通知 UI 更新按钮状态
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// 构造函数，初始化撤销/重做栈
    /// </summary>
    public UndoRedoManager()
    {
        _undoStack = new Stack<AnnotationOperation>();
        _redoStack = new Stack<AnnotationOperation>();
    }

    /// <summary>
    /// 记录添加笔画操作
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <param name="strokes">添加的笔画列表</param>
    public void RecordAddStrokes(uint pageIndex, IReadOnlyList<InkStroke> strokes)
    {
        if (strokes == null || strokes.Count == 0)
            return;

        // 克隆笔画以避免引用问题
        var clonedStrokes = new List<InkStroke>();
        foreach (var stroke in strokes)
        {
            clonedStrokes.Add(stroke.Clone());
        }

        var operation = new AnnotationOperation(
            AnnotationOperationType.AddStrokes,
            pageIndex,
            clonedStrokes);

        PushToUndoStack(operation);
        ClearRedoStack();
        OnStateChanged();
    }

    /// <summary>
    /// 记录删除笔画操作
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <param name="strokes">删除的笔画列表</param>
    public void RecordRemoveStrokes(uint pageIndex, IReadOnlyList<InkStroke> strokes)
    {
        if (strokes == null || strokes.Count == 0)
            return;

        // 克隆笔画以避免引用问题
        var clonedStrokes = new List<InkStroke>();
        foreach (var stroke in strokes)
        {
            clonedStrokes.Add(stroke.Clone());
        }

        var operation = new AnnotationOperation(
            AnnotationOperationType.RemoveStrokes,
            pageIndex,
            clonedStrokes);

        PushToUndoStack(operation);
        ClearRedoStack();
        OnStateChanged();
    }

    /// <summary>
    /// 撤销最后一个操作
    /// </summary>
    /// <returns>撤销的操作，如果撤销栈为空则返回 null</returns>
    public AnnotationOperation? Undo()
    {
        if (!CanUndo)
            return null;

        var operation = _undoStack.Pop();
        _redoStack.Push(operation);
        OnStateChanged();
        return operation;
    }

    /// <summary>
    /// 重做最后一个撤销的操作
    /// </summary>
    /// <returns>重做的操作，如果重做栈为空则返回 null</returns>
    public AnnotationOperation? Redo()
    {
        if (!CanRedo)
            return null;

        var operation = _redoStack.Pop();
        _undoStack.Push(operation);
        OnStateChanged();
        return operation;
    }

    /// <summary>
    /// 清空所有撤销和重做栈
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnStateChanged();
    }

    /// <summary>
    /// 清空重做栈
    /// </summary>
    public void ClearRedoStack()
    {
        if (_redoStack.Count > 0)
        {
            _redoStack.Clear();
            OnStateChanged();
        }
    }

    /// <summary>
    /// 将操作压入撤销栈，如果超过最大容量则移除最旧的操作
    /// </summary>
    /// <param name="operation">要压入的操作</param>
    private void PushToUndoStack(AnnotationOperation operation)
    {
        // 如果撤销栈已满，移除最旧的操作
        if (_undoStack.Count >= MaxUndoStackSize)
        {
            // 将栈转换为列表，移除最底部的元素，然后重建栈
            var operations = new List<AnnotationOperation>(_undoStack);
            operations.Reverse(); // 反转以获得正确的顺序（最旧的在前）
            operations.RemoveAt(0); // 移除最旧的操作
            operations.Reverse(); // 再次反转以恢复栈的顺序

            _undoStack.Clear();
            foreach (var op in operations)
            {
                _undoStack.Push(op);
            }
        }

        _undoStack.Push(operation);
    }

    /// <summary>
    /// 触发状态改变事件
    /// </summary>
    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
