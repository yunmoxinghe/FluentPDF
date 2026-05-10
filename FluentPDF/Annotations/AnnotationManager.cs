using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentPDF.Pages;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;

namespace FluentPDF.Annotations;

/// <summary>
/// 标注管理器，负责协调标注系统的所有组件
/// </summary>
public sealed class AnnotationManager : IDisposable
{
    // ── 依赖组件 ──
    private readonly PdfViewerPage _pdfViewer;
    private readonly Dictionary<uint, PageAnnotationData> _pageAnnotations;
    private UndoRedoManager? _undoRedoManager;
    private AnnotationStorage? _storage;
    private ToolState? _toolState;

    // ── 状态 ──
    private string? _pdfId;
    private uint _pageCount;
    private bool _isInitialized;
    private bool _isDisposed;
    private AnnotationTool? _previousToolBeforeEraser; // 用于触控笔橡皮擦端自动切换

    // ── 内存管理 ──
    private const int MaxLoadedPages = 5; // 最多同时加载 5 个页面的标注
    private readonly Queue<uint> _loadedPages = new Queue<uint>(); // LRU 队列

    /// <summary>
    /// 当前选中的工具
    /// </summary>
    public AnnotationTool CurrentTool => _toolState?.CurrentTool ?? AnnotationTool.None;

    /// <summary>
    /// 是否可以撤销
    /// </summary>
    public bool CanUndo => _undoRedoManager?.CanUndo ?? false;

    /// <summary>
    /// 是否可以重做
    /// </summary>
    public bool CanRedo => _undoRedoManager?.CanRedo ?? false;

    /// <summary>
    /// 工具改变事件
    /// </summary>
    public event EventHandler<ToolChangedEventArgs>? ToolChanged;

    /// <summary>
    /// 撤销/重做状态改变事件
    /// </summary>
    public event EventHandler<UndoRedoStateChangedEventArgs>? UndoRedoStateChanged;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="pdfViewer">PDF 查看器页面</param>
    public AnnotationManager(PdfViewerPage pdfViewer)
    {
        _pdfViewer = pdfViewer ?? throw new ArgumentNullException(nameof(pdfViewer));
        _pageAnnotations = new Dictionary<uint, PageAnnotationData>();
        _isInitialized = false;
        _isDisposed = false;
    }

    /// <summary>
    /// 初始化标注系统
    /// </summary>
    /// <param name="pdfId">PDF 文件的唯一标识符</param>
    /// <param name="pageCount">PDF 页面总数</param>
    public void Initialize(string pdfId, uint pageCount)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AnnotationManager));

        if (string.IsNullOrWhiteSpace(pdfId))
            throw new ArgumentException("PDF ID cannot be null or empty", nameof(pdfId));

        if (pageCount == 0)
            throw new ArgumentException("Page count must be greater than zero", nameof(pageCount));

        // 如果已经初始化，先清理
        if (_isInitialized)
        {
            Cleanup();
        }

        _pdfId = pdfId;
        _pageCount = pageCount;

        // 初始化组件
        _undoRedoManager = new UndoRedoManager();
        _storage = new AnnotationStorage(pdfId);
        _toolState = new ToolState();

        // 为每个页面创建标注数据容器
        _pageAnnotations.Clear();
        for (uint i = 0; i < pageCount; i++)
        {
            _pageAnnotations[i] = new PageAnnotationData(i);
        }

        // 订阅撤销/重做管理器的状态改变事件
        _undoRedoManager.StateChanged += OnUndoRedoStateChanged;

        _isInitialized = true;

        // 设置默认工具为硬笔（确保 InkPresenter 处于可用状态）
        SetTool(AnnotationTool.Pen);
    }

    /// <summary>
    /// 获取指定页面的标注数据
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <returns>页面标注数据</returns>
    public PageAnnotationData GetPageAnnotation(uint pageIndex)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AnnotationManager is not initialized");

        if (!_pageAnnotations.TryGetValue(pageIndex, out var pageData))
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range");

        return pageData;
    }

    /// <summary>
    /// 将 InkCanvas 与页面标注数据绑定
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <param name="inkCanvas">要绑定的 InkCanvas 控件</param>
    public void AttachInkCanvas(uint pageIndex, InkCanvas inkCanvas)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AnnotationManager is not initialized");

        if (inkCanvas == null)
            throw new ArgumentNullException(nameof(inkCanvas));

        var pageData = GetPageAnnotation(pageIndex);
        
        // 先取消订阅，避免重复订阅（元素回收再使用时）
        inkCanvas.InkPresenter.StrokesCollected -= OnStrokesCollected;
        inkCanvas.InkPresenter.StrokesErased -= OnStrokesErased;
        
        pageData.AttachCanvas(inkCanvas);

        // 配置 InkPresenter
        ConfigureInkPresenter(inkCanvas.InkPresenter);

        // 订阅笔画事件
        inkCanvas.InkPresenter.StrokesCollected += OnStrokesCollected;
        inkCanvas.InkPresenter.StrokesErased += OnStrokesErased;
    }

    /// <summary>
    /// 解除 InkCanvas 与页面标注数据的绑定
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    public void DetachInkCanvas(uint pageIndex)
    {
        if (!_isInitialized)
            return;

        if (_pageAnnotations.TryGetValue(pageIndex, out var pageData))
        {
            if (pageData.AttachedCanvas != null)
            {
                // 取消订阅事件
                pageData.AttachedCanvas.InkPresenter.StrokesCollected -= OnStrokesCollected;
                pageData.AttachedCanvas.InkPresenter.StrokesErased -= OnStrokesErased;

                pageData.DetachCanvas();
            }
        }
    }

    /// <summary>
    /// 保存所有已修改的页面标注
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task SaveAllAsync()
    {
        if (!_isInitialized || _storage == null)
            return;

        var modifiedPages = new List<PageAnnotationData>();
        foreach (var pageData in _pageAnnotations.Values)
        {
            if (pageData.IsModified)
            {
                modifiedPages.Add(pageData);
            }
        }

        if (modifiedPages.Count > 0)
        {
            await _storage.SaveAllAsync(modifiedPages);
        }
    }

    /// <summary>
    /// 加载指定页面的标注数据（带内存管理）
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <returns>异步任务</returns>
    public async Task LoadPageAsync(uint pageIndex)
    {
        if (!_isInitialized || _storage == null)
            return;

        var pageData = GetPageAnnotation(pageIndex);

        // 如果已经加载，更新 LRU 队列
        if (pageData.IsLoaded)
        {
            // 将页面移到队列末尾（最近使用）
            var tempQueue = new Queue<uint>();
            while (_loadedPages.Count > 0)
            {
                var page = _loadedPages.Dequeue();
                if (page != pageIndex)
                    tempQueue.Enqueue(page);
            }
            foreach (var page in tempQueue)
                _loadedPages.Enqueue(page);
            _loadedPages.Enqueue(pageIndex);
            return;
        }

        // 如果超过缓存上限，卸载最旧的页面
        if (_loadedPages.Count >= MaxLoadedPages)
        {
            uint oldestPage = _loadedPages.Dequeue();
            await UnloadPageAsync(oldestPage);
        }

        try
        {
            var container = await _storage.LoadPageAsync(pageIndex);
            if (container != null)
            {
                // 将加载的笔画添加到页面的 StrokeContainer
                foreach (var stroke in container.GetStrokes())
                {
                    pageData.StrokeContainer.AddStroke(stroke.Clone());
                }

                // 如果 InkCanvas 已经附加，应用笔画
                pageData.ApplyStrokesToCanvas();
            }

            pageData.IsLoaded = true;
            _loadedPages.Enqueue(pageIndex);
        }
        catch (InvalidOperationException ex)
        {
            // 加载失败时初始化空数据
            pageData.IsLoaded = true;
            _loadedPages.Enqueue(pageIndex);

            await Helpers.ErrorLogger.LogErrorAsync($"加载页面 {pageIndex} 标注失败", ex);

            // 显示警告消息给用户
            await _pdfViewer.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    var dialog = new Windows.UI.Xaml.Controls.ContentDialog
                    {
                        Title = "加载标注失败",
                        Content = $"无法加载第 {pageIndex + 1} 页的标注数据：{ex.Message}\n\n将显示空白标注层。",
                        CloseButtonText = "确定",
                        XamlRoot = _pdfViewer.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                catch
                {
                    // 忽略对话框显示失败
                }
            });
        }
        catch (Exception ex)
        {
            // 其他未预期的错误
            pageData.IsLoaded = true;
            _loadedPages.Enqueue(pageIndex);
            await Helpers.ErrorLogger.LogErrorAsync($"加载页面 {pageIndex} 标注时发生未知错误", ex);
        }
    }

    /// <summary>
    /// 卸载指定页面的标注数据以释放内存
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <returns>异步任务</returns>
    private async Task UnloadPageAsync(uint pageIndex)
    {
        if (!_isInitialized || _storage == null)
            return;

        var pageData = GetPageAnnotation(pageIndex);

        // 如果页面未加载，跳过
        if (!pageData.IsLoaded)
            return;

        try
        {
            // 如果页面已修改，先保存
            if (pageData.IsModified)
            {
                await _storage.SavePageAsync(pageIndex, pageData.StrokeContainer);
                pageData.IsModified = false;
                await Helpers.ErrorLogger.LogInfoAsync($"卸载前保存页面 {pageIndex} 的标注数据");
            }

            // 清空内存中的笔画数据
            pageData.StrokeContainer.Clear();
            pageData.IsLoaded = false;

            // 如果 InkCanvas 已附加，清空画布
            if (pageData.AttachedCanvas != null)
            {
                pageData.AttachedCanvas.InkPresenter.StrokeContainer.Clear();
            }
        }
        catch (Exception ex)
        {
            // 卸载失败时保留数据
            await Helpers.ErrorLogger.LogErrorAsync($"卸载页面 {pageIndex} 标注失败", ex);
            // 不抛出异常，避免影响其他页面的加载
        }
    }

    /// <summary>
    /// 切换标注工具
    /// </summary>
    /// <param name="tool">要切换到的工具</param>
    public void SetTool(AnnotationTool tool)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AnnotationManager is not initialized");

        if (_toolState == null)
            return;

        var oldTool = _toolState.CurrentTool;
        if (oldTool == tool)
            return;

        _toolState.CurrentTool = tool;

        // 更新所有已附加的 InkCanvas 的配置
        UpdateAllInkPresenters();

        // 触发工具改变事件
        ToolChanged?.Invoke(this, new ToolChangedEventArgs(oldTool, tool));
    }

    /// <summary>
    /// 设置当前工具的颜色
    /// </summary>
    /// <param name="color">要设置的颜色</param>
    public void SetToolColor(Windows.UI.Color color)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AnnotationManager is not initialized");

        if (_toolState == null)
            return;

        // 根据当前工具设置对应的颜色
        switch (_toolState.CurrentTool)
        {
            case AnnotationTool.Highlighter:
                _toolState.HighlighterColor = color;
                break;

            case AnnotationTool.Pen:
                _toolState.PenColor = color;
                break;

            case AnnotationTool.Eraser:
                // 橡皮擦不需要颜色
                return;

            case AnnotationTool.None:
                return;
        }

        // 更新所有已附加的 InkCanvas 的绘制属性
        UpdateAllInkPresenters();
    }

    /// <summary>
    /// 设置当前工具的粗细
    /// </summary>
    /// <param name="penTip">笔尖形状</param>
    /// <param name="size">笔触粗细</param>
    public void SetToolSize(Windows.UI.Input.Inking.PenTipShape penTip, double size)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AnnotationManager is not initialized");

        if (_toolState == null)
            return;

        if (size <= 0)
            throw new ArgumentException("Size must be greater than zero", nameof(size));

        // 根据当前工具设置对应的粗细
        switch (_toolState.CurrentTool)
        {
            case AnnotationTool.Highlighter:
                _toolState.HighlighterSize = size;
                break;

            case AnnotationTool.Pen:
                _toolState.PenSize = size;
                break;

            case AnnotationTool.Eraser:
                _toolState.EraserSize = size;
                break;

            case AnnotationTool.None:
                return;
        }

        // 更新所有已附加的 InkCanvas 的绘制属性
        UpdateAllInkPresenters();
    }

    /// <summary>
    /// 撤销最后一个标注操作
    /// </summary>
    public void Undo()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AnnotationManager is not initialized");

        if (_undoRedoManager == null)
            return;

        var operation = _undoRedoManager.Undo();
        if (operation != null)
        {
            ApplyOperation(operation, isUndo: true);
        }
    }

    /// <summary>
    /// 重做最后一个撤销的操作
    /// </summary>
    public void Redo()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AnnotationManager is not initialized");

        if (_undoRedoManager == null)
            return;

        var operation = _undoRedoManager.Redo();
        if (operation != null)
        {
            ApplyOperation(operation, isUndo: false);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        Cleanup();
        _isDisposed = true;
    }

    // ── 私有方法 ──

    /// <summary>
    /// 配置 InkPresenter 的输入设备和绘制属性
    /// </summary>
    /// <param name="inkPresenter">InkPresenter 实例</param>
    private void ConfigureInkPresenter(InkPresenter inkPresenter)
    {
        // 支持鼠标、触控笔和触摸输入
        inkPresenter.InputDeviceTypes =
            Windows.UI.Core.CoreInputDeviceTypes.Mouse |
            Windows.UI.Core.CoreInputDeviceTypes.Pen |
            Windows.UI.Core.CoreInputDeviceTypes.Touch;

        // 订阅未处理输入事件，用于检测触控笔橡皮擦端
        inkPresenter.UnprocessedInput.PointerEntered += OnUnprocessedPointerEntered;

        // 应用当前工具的绘制属性
        ApplyCurrentToolAttributes(inkPresenter);
    }

    /// <summary>
    /// 处理未处理的指针进入事件，检测触控笔橡皮擦端
    /// </summary>
    private void OnUnprocessedPointerEntered(Windows.UI.Input.Inking.InkUnprocessedInput sender, Windows.UI.Core.PointerEventArgs args)
    {
        try
        {
            // 检查是否为触控笔的橡皮擦端
            if (args.CurrentPoint.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                // 检查是否为橡皮擦端（IsEraser 属性）
                if (args.CurrentPoint.Properties.IsEraser)
                {
                    // 自动切换到橡皮擦工具
                    if (_toolState != null && _toolState.CurrentTool != AnnotationTool.Eraser)
                    {
                        _previousToolBeforeEraser = _toolState.CurrentTool;
                        SetTool(AnnotationTool.Eraser);
                    }
                }
                else
                {
                    // 如果之前自动切换到了橡皮擦，现在切换回之前的工具
                    if (_previousToolBeforeEraser.HasValue && _toolState != null && _toolState.CurrentTool == AnnotationTool.Eraser)
                    {
                        SetTool(_previousToolBeforeEraser.Value);
                        _previousToolBeforeEraser = null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 捕获异常，避免影响主流程
            System.Diagnostics.Debug.WriteLine($"[OnUnprocessedPointerEntered] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// 更新所有已附加的 InkCanvas 的 InkPresenter 配置
    /// </summary>
    private void UpdateAllInkPresenters()
    {
        foreach (var pageData in _pageAnnotations.Values)
        {
            if (pageData.AttachedCanvas != null)
            {
                ApplyCurrentToolAttributes(pageData.AttachedCanvas.InkPresenter);
            }
        }
    }

    /// <summary>
    /// 应用当前工具的绘制属性到 InkPresenter
    /// </summary>
    /// <param name="inkPresenter">InkPresenter 实例</param>
    private void ApplyCurrentToolAttributes(InkPresenter inkPresenter)
    {
        if (_toolState == null)
            return;

        switch (_toolState.CurrentTool)
        {
            case AnnotationTool.Highlighter:
                // 设置为绘制模式
                inkPresenter.InputProcessingConfiguration.Mode = Windows.UI.Input.Inking.InkInputProcessingMode.Inking;
                
                var highlighterAttributes = inkPresenter.CopyDefaultDrawingAttributes();
                highlighterAttributes.Color = _toolState.HighlighterColor;
                highlighterAttributes.Size = new Windows.Foundation.Size(_toolState.HighlighterSize, _toolState.HighlighterSize);
                highlighterAttributes.PenTip = Windows.UI.Input.Inking.PenTipShape.Rectangle;
                highlighterAttributes.DrawAsHighlighter = true;
                highlighterAttributes.IgnorePressure = true; // 荧光笔忽略压感
                inkPresenter.UpdateDefaultDrawingAttributes(highlighterAttributes);
                break;

            case AnnotationTool.Pen:
                // 设置为绘制模式
                inkPresenter.InputProcessingConfiguration.Mode = Windows.UI.Input.Inking.InkInputProcessingMode.Inking;
                
                var penAttributes = inkPresenter.CopyDefaultDrawingAttributes();
                penAttributes.Color = _toolState.PenColor;
                penAttributes.Size = new Windows.Foundation.Size(_toolState.PenSize, _toolState.PenSize);
                penAttributes.PenTip = Windows.UI.Input.Inking.PenTipShape.Circle;
                penAttributes.DrawAsHighlighter = false;
                penAttributes.IgnorePressure = false; // 硬笔启用压感检测
                penAttributes.FitToCurve = true; // 平滑曲线
                inkPresenter.UpdateDefaultDrawingAttributes(penAttributes);
                break;

            case AnnotationTool.Eraser:
                // 设置为擦除模式
                inkPresenter.InputProcessingConfiguration.Mode = Windows.UI.Input.Inking.InkInputProcessingMode.Erasing;
                
                // 橡皮擦模式下不需要设置 DrawingAttributes
                // InkPresenter 会自动处理擦除行为
                break;

            case AnnotationTool.None:
                // 设置为无模式（查看模式）
                inkPresenter.InputProcessingConfiguration.Mode = Windows.UI.Input.Inking.InkInputProcessingMode.None;
                break;
        }
    }

    /// <summary>
    /// 笔画收集事件处理器
    /// </summary>
    private void OnStrokesCollected(InkPresenter sender, InkStrokesCollectedEventArgs args)
    {
        if (_undoRedoManager == null)
            return;

        // 查找对应的页面
        uint? pageIndex = FindPageIndexByInkPresenter(sender);
        if (pageIndex.HasValue)
        {
            var strokes = args.Strokes;
            _undoRedoManager.RecordAddStrokes(pageIndex.Value, strokes);

            // 标记页面为已修改
            if (_pageAnnotations.TryGetValue(pageIndex.Value, out var pageData))
            {
                pageData.IsModified = true;
            }
        }
    }

    /// <summary>
    /// 笔画擦除事件处理器
    /// </summary>
    private void OnStrokesErased(InkPresenter sender, InkStrokesErasedEventArgs args)
    {
        if (_undoRedoManager == null)
            return;

        // 查找对应的页面
        uint? pageIndex = FindPageIndexByInkPresenter(sender);
        if (pageIndex.HasValue)
        {
            var strokes = args.Strokes;
            _undoRedoManager.RecordRemoveStrokes(pageIndex.Value, strokes);

            // 标记页面为已修改
            if (_pageAnnotations.TryGetValue(pageIndex.Value, out var pageData))
            {
                pageData.IsModified = true;
            }
        }
    }

    /// <summary>
    /// 根据 InkPresenter 查找对应的页面索引
    /// </summary>
    /// <param name="inkPresenter">InkPresenter 实例</param>
    /// <returns>页面索引，如果未找到则返回 null</returns>
    private uint? FindPageIndexByInkPresenter(InkPresenter inkPresenter)
    {
        foreach (var kvp in _pageAnnotations)
        {
            if (kvp.Value.AttachedCanvas?.InkPresenter == inkPresenter)
            {
                return kvp.Key;
            }
        }
        return null;
    }

    /// <summary>
    /// 应用撤销/重做操作到 InkCanvas
    /// </summary>
    /// <param name="operation">要应用的操作</param>
    /// <param name="isUndo">是否为撤销操作（true 为撤销，false 为重做）</param>
    private void ApplyOperation(AnnotationOperation operation, bool isUndo)
    {
        if (!_pageAnnotations.TryGetValue(operation.PageIndex, out var pageData))
            return;

        // 根据操作类型和是否为撤销操作，决定是添加还是删除笔画
        bool shouldAdd = (operation.Type == AnnotationOperationType.AddStrokes && !isUndo) ||
                         (operation.Type == AnnotationOperationType.RemoveStrokes && isUndo);

        if (shouldAdd)
        {
            // 添加笔画 - 使用克隆的笔画
            foreach (var stroke in operation.Strokes)
            {
                pageData.StrokeContainer.AddStroke(stroke.Clone());
            }
        }
        else
        {
            // 删除笔画 - 需要找到容器中对应的笔画并删除
            // 由于我们存储的是克隆的笔画，需要通过笔画的属性来匹配
            var allStrokes = pageData.StrokeContainer.GetStrokes().ToList();
            
            // 从后往前删除，避免索引变化
            for (int i = allStrokes.Count - 1; i >= 0 && operation.Strokes.Count > 0; i--)
            {
                // 简单策略：删除最后添加的 N 个笔画（N = operation.Strokes.Count）
                // 这适用于连续的撤销操作
                if (allStrokes.Count - i <= operation.Strokes.Count)
                {
                    allStrokes[i].Selected = true;
                }
            }
            pageData.StrokeContainer.DeleteSelected();
        }

        // 标记页面为已修改
        pageData.IsModified = true;

        // 如果 InkCanvas 已附加，更新显示
        pageData.ApplyStrokesToCanvas();
    }

    /// <summary>
    /// 撤销/重做状态改变事件处理器
    /// </summary>
    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        UndoRedoStateChanged?.Invoke(this, new UndoRedoStateChangedEventArgs(CanUndo, CanRedo));
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private void Cleanup()
    {
        // 取消订阅事件
        if (_undoRedoManager != null)
        {
            _undoRedoManager.StateChanged -= OnUndoRedoStateChanged;
        }

        // 解除所有 InkCanvas 绑定
        foreach (var pageData in _pageAnnotations.Values)
        {
            if (pageData.AttachedCanvas != null)
            {
                pageData.AttachedCanvas.InkPresenter.StrokesCollected -= OnStrokesCollected;
                pageData.AttachedCanvas.InkPresenter.StrokesErased -= OnStrokesErased;
                pageData.DetachCanvas();
            }
        }

        _pageAnnotations.Clear();
        _undoRedoManager = null;
        _storage = null;
        _toolState = null;
        _isInitialized = false;
    }
}

/// <summary>
/// 工具改变事件参数
/// </summary>
public sealed class ToolChangedEventArgs : EventArgs
{
    public AnnotationTool OldTool { get; }
    public AnnotationTool NewTool { get; }

    public ToolChangedEventArgs(AnnotationTool oldTool, AnnotationTool newTool)
    {
        OldTool = oldTool;
        NewTool = newTool;
    }
}

/// <summary>
/// 撤销/重做状态改变事件参数
/// </summary>
public sealed class UndoRedoStateChangedEventArgs : EventArgs
{
    public bool CanUndo { get; }
    public bool CanRedo { get; }

    public UndoRedoStateChangedEventArgs(bool canUndo, bool canRedo)
    {
        CanUndo = canUndo;
        CanRedo = canRedo;
    }
}
