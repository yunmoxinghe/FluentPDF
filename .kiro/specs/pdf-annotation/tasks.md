# Implementation Plan: PDF 标注功能

## Overview

本实现计划将 PDF 标注功能集成到 FluentPDF 应用程序中。该功能基于 UWP InkCanvas 和 InkPresenter API，提供荧光笔、硬笔和橡皮擦三种标注工具。实现采用分层架构，包括 UI 层（XAML）、标注管理层（AnnotationManager）和存储层（AnnotationStorage），确保与现有 PdfViewerPage 组件的无缝集成。

## Tasks

- [x] 1. 创建核心数据模型和枚举类型
  - 创建 `Annotations` 文件夹用于存放标注相关代码
  - 实现 `AnnotationTool` 枚举（None, Highlighter, Pen, Eraser）
  - 实现 `AnnotationOperationType` 枚举（AddStrokes, RemoveStrokes）
  - 实现 `AnnotationOperation` 类，包含操作类型、页面索引、笔画列表和时间戳
  - 实现 `ToolState` 类，管理当前工具和各工具的颜色、粗细配置
  - 实现 `ToolConfiguration` 静态类，定义荧光笔、硬笔和橡皮擦的预设颜色和粗细
  - _Requirements: 1.1, 1.3, 1.4, 2.1, 2.3, 2.4, 3.1, 3.3, 4.1, 5.1, 5.3_

- [x] 2. 实现 PageAnnotationData 类
  - [x] 2.1 创建 PageAnnotationData 类的基本结构
    - 实现构造函数，初始化 PageIndex 和 InkStrokeContainer
    - 实现 IsModified 和 IsLoaded 状态标记属性
    - 实现 AttachedCanvas 属性用于 UI 绑定
    - _Requirements: 6.1, 6.2_
  
  - [x] 2.2 实现 InkCanvas 绑定方法
    - 实现 `AttachCanvas(InkCanvas canvas)` 方法，将 InkCanvas 与 StrokeContainer 绑定
    - 实现 `DetachCanvas()` 方法，解除 InkCanvas 绑定并清理资源
    - 实现 `ApplyStrokesToCanvas()` 方法，将笔画数据渲染到 InkCanvas
    - 实现 `GetStrokes()` 方法，返回当前页面的所有笔画
    - _Requirements: 6.2, 6.3, 8.3_

- [x] 3. 实现 UndoRedoManager 类
  - [x] 3.1 创建撤销/重做栈管理器
    - 实现 CanUndo 和 CanRedo 属性
    - 实现 UndoStackCount 和 RedoStackCount 属性
    - 使用 Stack<AnnotationOperation> 存储撤销和重做操作
    - 实现 StateChanged 事件，通知 UI 更新按钮状态
    - _Requirements: 9.1, 9.6, 9.9, 9.10_
  
  - [x] 3.2 实现操作记录方法
    - 实现 `RecordAddStrokes(uint pageIndex, IReadOnlyList<InkStroke> strokes)` 方法
    - 实现 `RecordRemoveStrokes(uint pageIndex, IReadOnlyList<InkStroke> strokes)` 方法
    - 记录操作时清空重做栈
    - _Requirements: 9.1, 9.5_
  
  - [x] 3.3 实现撤销/重做逻辑
    - 实现 `Undo()` 方法，从撤销栈弹出操作并执行反向操作
    - 实现 `Redo()` 方法，从重做栈弹出操作并重新执行
    - 实现 `Clear()` 和 `ClearRedoStack()` 方法
    - 触发 StateChanged 事件更新 UI
    - _Requirements: 9.2, 9.3, 9.4_

- [x] 4. 实现 AnnotationStorage 类
  - [x] 4.1 创建文件路径管理
    - 实现构造函数，接收 pdfId 参数
    - 实现 `GetAnnotationFolderPath()` 方法，返回标注文件夹路径
    - 实现 `GetPageAnnotationFilePath(uint pageIndex)` 方法，生成页面标注文件路径
    - 实现 PDF 文件标识符生成方法（使用 SHA256 哈希）
    - _Requirements: 7.3, 13.4, 13.5, 13.6_
  
  - [x] 4.2 实现标注数据保存
    - 实现 `SavePageAsync(uint pageIndex, InkStrokeContainer container)` 方法
    - 使用 InkStrokeContainer.SaveAsync() API 序列化为 ISF 格式
    - 实现 `SaveAllAsync(IEnumerable<PageAnnotationData> pages)` 方法
    - 处理文件夹不存在的情况，自动创建文件夹
    - 添加异步操作和错误处理
    - _Requirements: 7.1, 7.2, 7.4, 7.6, 13.1, 13.2, 13.7, 14.1_
  
  - [x] 4.3 实现标注数据加载
    - 实现 `LoadPageAsync(uint pageIndex)` 方法
    - 使用 InkStrokeContainer.LoadAsync() API 反序列化 ISF 文件
    - 实现 `HasAnnotationsAsync(uint pageIndex)` 方法，检查标注文件是否存在
    - 处理文件不存在和文件损坏的情况
    - 添加异步操作和错误处理
    - _Requirements: 8.1, 8.2, 8.4, 8.6, 13.3, 14.2, 14.3_
  
  - [x] 4.4 实现标注数据删除
    - 实现 `DeletePageAsync(uint pageIndex)` 方法
    - 实现 `DeleteAllAsync()` 方法
    - 添加错误处理
    - _Requirements: 14.1_

- [x] 5. 实现 AnnotationManager 核心协调器
  - [x] 5.1 创建 AnnotationManager 基本结构
    - 实现构造函数，接收 PdfViewerPage 参数
    - 实现 `Initialize(string pdfId, uint pageCount)` 方法
    - 创建 PageAnnotationData 字典，为每个页面初始化标注数据容器
    - 初始化 UndoRedoManager 和 AnnotationStorage 实例
    - 初始化 ToolState 实例
    - 实现 IDisposable 接口和 Dispose() 方法
    - _Requirements: 6.1, 10.1_
  
  - [x] 5.2 实现工具管理
    - 实现 CurrentTool 属性
    - 实现 `SetTool(AnnotationTool tool)` 方法，切换标注工具
    - 实现 `SetToolColor(Color color)` 方法，设置当前工具颜色
    - 实现 `SetToolSize(InkDrawingAttributes.PenTip penTip, double size)` 方法
    - 更新 InkPresenter 的 InputProcessingConfiguration
    - 触发 ToolChanged 事件
    - _Requirements: 1.1, 2.1, 3.1, 4.2, 4.3, 5.2, 5.4, 5.5_
  
  - [x] 5.3 实现页面标注管理
    - 实现 `GetPageAnnotation(uint pageIndex)` 方法
    - 实现 `AttachInkCanvas(uint pageIndex, InkCanvas inkCanvas)` 方法
    - 实现 `DetachInkCanvas(uint pageIndex)` 方法
    - 配置 InkPresenter 的 InputDeviceTypes（支持鼠标、触控笔、触摸）
    - 订阅 InkPresenter.StrokesCollected 和 StrokesErased 事件
    - _Requirements: 1.6, 2.6, 3.5, 6.2, 10.2, 12.1, 12.2, 12.3_
  
  - [x] 5.4 实现笔画事件处理
    - 实现 StrokesCollected 事件处理器，记录添加操作到撤销栈
    - 实现 StrokesErased 事件处理器，记录删除操作到撤销栈
    - 标记页面为已修改（IsModified = true）
    - _Requirements: 7.1, 9.1_
  
  - [x] 5.5 实现撤销/重做功能
    - 实现 CanUndo 和 CanRedo 属性，绑定到 UndoRedoManager
    - 实现 `Undo()` 方法，调用 UndoRedoManager.Undo() 并应用到 InkCanvas
    - 实现 `Redo()` 方法，调用 UndoRedoManager.Redo() 并应用到 InkCanvas
    - 触发 UndoRedoStateChanged 事件
    - _Requirements: 9.2, 9.3, 9.4_
  
  - [x] 5.6 实现持久化功能
    - 实现 `SaveAllAsync()` 方法，遍历所有已修改页面并保存
    - 实现 `LoadPageAsync(uint pageIndex)` 方法，异步加载页面标注数据
    - 实现内存管理策略，仅加载可见页面和相邻页面
    - 实现页面卸载逻辑，保存已修改页面后清空内存
    - _Requirements: 7.2, 7.6, 8.2, 8.6, 11.4, 11.5, 11.6_

- [x] 6. Checkpoint - 核心组件完成
  - 确保所有核心类（PageAnnotationData, UndoRedoManager, AnnotationStorage, AnnotationManager）编译通过
  - 如有问题，询问用户

- [x] 7. 在 PdfViewerPage.xaml 中添加 UI 元素
  - [x] 7.1 添加撤销/重做按钮到工具栏
    - 在 CatalogButton 之后添加 UndoButton
    - 在 UndoButton 之后添加 RedoButton
    - 使用 Segoe MDL2 Assets 字体图标（撤销: &#xE7A7;, 重做: &#xE7A6;）
    - 绑定 IsEnabled 属性到 AnnotationManager.CanUndo 和 CanRedo
    - 添加工具提示（撤销 (Ctrl+Z), 重做 (Ctrl+Y)）
    - 在撤销/重做按钮和标注工具之间添加视觉分隔符
    - _Requirements: 9.7, 9.8, 9.9, 9.10, 15.2, 15.10, 15.13_
  
  - [x] 7.2 添加标注工具按钮到工具栏
    - 在分隔符之后添加 HighlighterButton（InkToolbarCustomPenButton）
    - 添加 PenButton（InkToolbarCustomPenButton）
    - 添加 EraserButton（InkToolbarEraserButton）
    - 使用 SymbolIcon 设置图标（Highlight, Edit）
    - 添加工具提示（荧光笔、硬笔、橡皮擦）
    - _Requirements: 4.1, 15.1, 15.2, 15.10_
  
  - [x] 7.3 创建工具配置面板资源
    - 在 Page.Resources 中定义 HighlighterConfiguration StackPanel
    - 添加颜色调色板 GridView，使用圆形色块显示预设颜色
    - 添加粗细选择 Slider（荧光笔: 8-24, 硬笔: 1-8）
    - 在 Page.Resources 中定义 PenConfiguration StackPanel
    - 添加颜色调色板和粗细选择器
    - 绑定 ConfigurationContent 到工具按钮
    - _Requirements: 1.3, 1.4, 2.3, 2.4, 5.1, 5.3, 15.3, 15.4, 15.7, 15.8_

- [x] 8. 修改 PdfPageView 组件集成 InkCanvas
  - [x] 8.1 在 PdfPageView.xaml 中添加 InkCanvas 覆盖层
    - 在 PageContainer Grid 中添加 InkCanvas 元素
    - 设置 Width 和 Height 绑定到 DisplayWidth 和 DisplayHeight
    - 设置 Background="Transparent"
    - 设置 HorizontalAlignment="Center" 和 VerticalAlignment="Center"
    - 确保 InkCanvas 在 PDF 渲染层之上
    - _Requirements: 6.2, 6.3, 10.1_
  
  - [x] 8.2 在 PdfPageView.xaml.cs 中实现 InkCanvas 初始化
    - 添加 `InitializeAnnotationCanvas(PageAnnotationData annotationData)` 方法
    - 配置 InkPresenter.InputDeviceTypes（Mouse, Pen, Touch）
    - 订阅 StrokesCollected 和 StrokesErased 事件
    - 调用 annotationData.AttachCanvas(AnnotationCanvas)
    - _Requirements: 1.6, 2.6, 3.5, 6.2, 12.1, 12.2, 12.3_

- [x] 9. 在 PdfViewerPage.xaml.cs 中集成标注系统
  - [x] 9.1 初始化 AnnotationManager
    - 在 PdfViewerPage 类中添加 AnnotationManager 字段
    - 在 LoadFileAsync 方法中初始化 AnnotationManager
    - 调用 AnnotationManager.Initialize(pdfId, pageCount)
    - 生成 PDF 文件标识符（使用 SHA256 哈希）
    - _Requirements: 8.1, 10.1_
  
  - [x] 9.2 实现页面标注绑定
    - 在 PagesRepeater 的 ElementPrepared 事件中绑定 InkCanvas
    - 调用 AnnotationManager.AttachInkCanvas(pageIndex, inkCanvas)
    - 调用 AnnotationManager.LoadPageAsync(pageIndex) 加载标注数据
    - 在 ElementClearing 事件中解除绑定
    - _Requirements: 6.5, 8.2, 8.3, 10.5_
  
  - [x] 9.3 实现工具按钮事件处理
    - 实现 HighlighterButton_Click 事件处理器
    - 实现 PenButton_Click 事件处理器
    - 实现 EraserButton_Click 事件处理器
    - 调用 AnnotationManager.SetTool() 切换工具
    - 更新按钮选中状态
    - _Requirements: 1.1, 2.1, 3.1, 4.2, 4.3_
  
  - [x] 9.4 实现颜色和粗细选择事件处理
    - 实现 HighlighterColorPalette_SelectionChanged 事件处理器
    - 实现 PenColorPalette_SelectionChanged 事件处理器
    - 实现 HighlighterSizeSlider_ValueChanged 事件处理器
    - 实现 PenSizeSlider_ValueChanged 事件处理器
    - 调用 AnnotationManager.SetToolColor() 和 SetToolSize()
    - 更新按钮图标颜色指示器
    - _Requirements: 5.2, 5.4, 5.5, 5.6, 15.3, 15.4_
  
  - [x] 9.5 实现撤销/重做按钮事件处理
    - 实现 UndoButton_Click 事件处理器，调用 AnnotationManager.Undo()
    - 实现 RedoButton_Click 事件处理器，调用 AnnotationManager.Redo()
    - 添加键盘快捷键支持（Ctrl+Z, Ctrl+Y）
    - _Requirements: 9.2, 9.4, 4.5_
  
  - [x] 9.6 实现标注保存和清理
    - 在文件关闭时调用 AnnotationManager.SaveAllAsync()
    - 在 CancelAll 方法中调用 AnnotationManager.Dispose()
    - 添加错误处理和用户提示
    - _Requirements: 7.2, 7.5, 14.1_

- [ ] 10. 实现缩放和旋转同步
  - [ ] 10.1 实现缩放同步
    - 订阅 PdfScrollViewer.ViewChanged 事件
    - 同步更新所有 InkCanvas 的 Width 和 Height
    - InkPresenter 自动处理笔画的缩放变换
    - _Requirements: 6.3, 10.2_
  
  - [~] 10.2 实现旋转同步
    - 订阅 RotateButton_Click 事件
    - 对每个页面的 InkCanvas 应用 RotateTransform
    - 保持笔画相对于 PDF 内容的正确方向
    - _Requirements: 6.4, 10.2_

- [ ] 11. 实现触控笔特殊功能
  - [~] 11.1 实现压感检测
    - 在 Pen_Tool 中启用压感检测
    - 配置 InkDrawingAttributes.IgnorePressure = false
    - 根据压感值调整笔画粗细
    - _Requirements: 2.5, 12.4_
  
  - [~] 11.2 实现触控笔橡皮擦端自动切换
    - 订阅 InkPresenter.InputDeviceTypes 变化事件
    - 检测触控笔橡皮擦端输入
    - 自动切换到 Eraser_Tool
    - _Requirements: 3.6, 12.5_

- [ ] 12. 实现性能优化
  - [~] 12.1 实现延迟加载策略
    - 监听 PdfScrollViewer 滚动事件
    - 仅加载可见页面和相邻页面的标注数据
    - 卸载离开视口的页面标注（保留数据）
    - _Requirements: 6.5, 11.4, 11.5_
  
  - [~] 12.2 实现内存管理
    - 实现 AnnotationMemoryManager 类（可选）
    - 限制同时加载的页面数量（最多 5 个）
    - 卸载页面前自动保存已修改数据
    - _Requirements: 11.5, 11.6_

- [~] 13. Checkpoint - 功能集成完成
  - 确保所有 UI 元素和事件处理器正确连接
  - 测试基本标注功能（绘制、擦除、撤销、重做）
  - 测试标注保存和加载
  - 如有问题，询问用户

- [ ] 14. 实现错误处理和用户提示
  - [~] 14.1 添加保存失败处理
    - 在 AnnotationStorage.SavePageAsync 中捕获异常
    - 显示错误消息对话框
    - 保留内存中的标注数据
    - _Requirements: 7.5, 14.1_
  
  - [~] 14.2 添加加载失败处理
    - 在 AnnotationStorage.LoadPageAsync 中捕获异常
    - 显示警告消息对话框
    - 初始化空的标注数据
    - _Requirements: 8.5, 14.2_
  
  - [~] 14.3 添加文件损坏处理
    - 捕获 ISF 反序列化异常
    - 尝试恢复部分数据
    - 记录错误日志
    - _Requirements: 14.3_
  
  - [~] 14.4 添加磁盘空间不足处理
    - 捕获磁盘空间不足异常
    - 显示错误消息
    - 阻止新的标注操作
    - _Requirements: 14.4_
  
  - [~] 14.5 实现错误日志记录
    - 创建日志记录工具类
    - 记录所有错误到应用程序日志文件
    - _Requirements: 14.6_

- [ ] 15. 实现 UI 主题和样式
  - [~] 15.1 实现工具按钮选中状态样式
    - 定义选中状态的视觉样式（浅灰色背景或底部边框）
    - 应用到 HighlighterButton, PenButton, EraserButton
    - _Requirements: 15.6_
  
  - [~] 15.2 实现按钮图标颜色指示器
    - 在荧光笔和硬笔按钮图标上显示当前选中颜色
    - 当用户更改颜色时立即更新图标
    - _Requirements: 15.3, 15.4, 15.5_
  
  - [~] 15.3 实现浅色和深色主题支持
    - 使用 ThemeResource 定义颜色和样式
    - 测试浅色和深色主题下的 UI 显示
    - _Requirements: 15.11_
  
  - [~] 15.4 实现触摸目标尺寸
    - 确保所有工具按钮至少 44x44 像素
    - 测试触摸设备上的可用性
    - _Requirements: 15.12_

- [ ] 16. 最终测试和验证
  - [~] 16.1 功能测试
    - 测试荧光笔、硬笔、橡皮擦工具的基本功能
    - 测试工具切换和颜色/粗细选择
    - 测试撤销/重做功能
    - 测试标注保存和加载
    - 测试多页面标注
  
  - [~] 16.2 集成测试
    - 测试与现有 PDF 查看器功能的兼容性（缩放、滚动、旋转、单双页切换）
    - 测试不同输入设备（鼠标、触控笔、触摸）
    - 测试触控笔压感和橡皮擦端
  
  - [~] 16.3 性能测试
    - 测试绘制流畅度（60 FPS）
    - 测试工具切换响应时间（< 100ms）
    - 测试大量笔画的加载和渲染性能
    - 测试内存使用情况
  
  - [~] 16.4 错误处理测试
    - 测试保存失败场景
    - 测试加载失败场景
    - 测试文件损坏场景
    - 测试磁盘空间不足场景

- [~] 17. Final Checkpoint - 确保所有测试通过
  - 确保所有功能正常工作
  - 确保性能满足要求
  - 确保错误处理正确
  - 如有问题，询问用户

## Notes

- 本实现计划基于 UWP InkCanvas 和 InkPresenter API，充分利用 Windows Ink 平台的特性
- 标注数据使用 ISF (Ink Serialized Format) 格式保存，确保跨会话的数据完整性
- 采用延迟加载和内存管理策略，避免大文件内存溢出
- 所有 UI 操作在 UI 线程执行，文件 I/O 使用异步操作
- 与现有 PdfViewerPage 组件深度集成，保持所有现有功能
- 每个任务引用具体的需求编号，确保需求可追溯性
