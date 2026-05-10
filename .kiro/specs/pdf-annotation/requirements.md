# Requirements Document

## Introduction

本文档定义 FluentPDF 应用程序的 PDF 标注功能需求。该功能将为用户提供与 Microsoft Edge 浏览器一致的三种标注工具：荧光笔、硬笔和橡皮擦。标注功能需要支持触控笔、鼠标和触摸输入，并能够保存和加载标注数据。

## Glossary

- **Annotation_System**: PDF 标注系统，负责管理所有标注工具和标注数据
- **Highlighter_Tool**: 荧光笔工具，用于高亮标记 PDF 文本和区域
- **Pen_Tool**: 硬笔工具，用于手写笔记和绘图
- **Eraser_Tool**: 橡皮擦工具，用于擦除已有的标注
- **Annotation_Layer**: 标注层，覆盖在 PDF 页面上的透明层，用于渲染标注内容
- **Annotation_Data**: 标注数据，包含笔画路径、颜色、粗细等信息
- **Input_Device**: 输入设备，包括触控笔、鼠标和触摸屏
- **Stroke**: 笔画，用户在标注层上绘制的单个连续路径
- **Tool_Palette**: 工具面板，用于选择标注工具、颜色和笔触粗细
- **Annotation_Storage**: 标注存储，负责将标注数据持久化到文件系统，使用 ISF 格式
- **Undo_Stack**: 撤销栈，记录标注操作历史以支持撤销功能
- **Redo_Stack**: 重做栈，记录已撤销的操作以支持重做功能
- **PDF_Viewer**: PDF 查看器，现有的 PdfViewerPage 组件
- **InkCanvas**: UWP 平台的墨迹画布控件，用于捕获和渲染笔画
- **InkPresenter**: UWP 平台的墨迹呈现器，管理 InkCanvas 的输入、处理和渲染
- **InkStrokeContainer**: UWP 平台的墨迹笔画容器，管理 InkStroke 集合并提供序列化功能
- **InkToolbar**: UWP 平台的墨迹工具栏控件，提供工具选择界面
- **ISF**: Ink Serialized Format，Windows Ink 的标准序列化格式，是包含墨迹元数据的 GIF 文件

## Requirements

### Requirement 1: 荧光笔工具

**User Story:** 作为用户，我想使用荧光笔高亮标记 PDF 中的重要文本和区域，以便快速识别关键内容。

#### Acceptance Criteria

1. WHEN 用户选择荧光笔工具，THE Annotation_System SHALL 激活 Highlighter_Tool 并设置半透明绘制模式
2. WHEN 用户使用 Input_Device 在 Annotation_Layer 上绘制，THE Highlighter_Tool SHALL 创建半透明的 Stroke 并实时渲染
3. THE Highlighter_Tool SHALL 支持至少 4 种预设颜色（黄色、绿色、蓝色、粉色）
4. THE Highlighter_Tool SHALL 支持至少 3 种笔触粗细（细、中、粗）
5. WHEN 用户绘制荧光笔笔画，THE Stroke SHALL 具有 40% 到 60% 的不透明度
6. THE Highlighter_Tool SHALL 支持触控笔、鼠标和触摸输入

### Requirement 2: 硬笔工具

**User Story:** 作为用户，我想使用硬笔在 PDF 上书写笔记和绘图，以便添加个人注释和说明。

#### Acceptance Criteria

1. WHEN 用户选择硬笔工具，THE Annotation_System SHALL 激活 Pen_Tool 并设置不透明绘制模式
2. WHEN 用户使用 Input_Device 在 Annotation_Layer 上绘制，THE Pen_Tool SHALL 创建不透明的 Stroke 并实时渲染
3. THE Pen_Tool SHALL 支持至少 6 种预设颜色（黑色、红色、蓝色、绿色、黄色、白色）
4. THE Pen_Tool SHALL 支持至少 3 种笔触粗细（细、中、粗）
5. WHEN 用户使用触控笔绘制，THE Pen_Tool SHALL 支持压感检测并调整笔画粗细
6. THE Pen_Tool SHALL 支持触控笔、鼠标和触摸输入

### Requirement 3: 橡皮擦工具

**User Story:** 作为用户，我想使用橡皮擦删除错误的标注，以便修正我的标记。

#### Acceptance Criteria

1. WHEN 用户选择橡皮擦工具，THE Annotation_System SHALL 激活 Eraser_Tool
2. WHEN 用户使用 Input_Device 在 Annotation_Layer 上移动橡皮擦，THE Eraser_Tool SHALL 删除与橡皮擦路径相交的 Stroke
3. THE Eraser_Tool SHALL 支持至少 2 种橡皮擦尺寸（小、大）
4. WHEN 橡皮擦删除 Stroke，THE Annotation_System SHALL 立即更新 Annotation_Layer 的渲染
5. THE Eraser_Tool SHALL 支持触控笔、鼠标和触摸输入
6. WHEN 用户使用触控笔的橡皮擦端，THE Annotation_System SHALL 自动切换到 Eraser_Tool

### Requirement 4: 工具切换

**User Story:** 作为用户，我想在不同的标注工具之间快速切换，以便高效地完成标注任务。

#### Acceptance Criteria

1. THE Tool_Palette SHALL 显示所有可用的标注工具（荧光笔、硬笔、橡皮擦）
2. WHEN 用户点击工具图标，THE Annotation_System SHALL 切换到选定的工具并更新 Tool_Palette 的视觉状态
3. WHEN 工具切换完成，THE Annotation_System SHALL 在 200 毫秒内响应用户的下一次输入
4. THE Tool_Palette SHALL 显示当前选中工具的高亮状态
5. WHEN 用户使用键盘快捷键，THE Annotation_System SHALL 切换到对应的工具

### Requirement 5: 颜色和粗细选择

**User Story:** 作为用户，我想自定义标注的颜色和粗细，以便根据不同的标注目的使用不同的样式。

#### Acceptance Criteria

1. THE Tool_Palette SHALL 显示当前工具的可用颜色选项
2. WHEN 用户选择颜色，THE Annotation_System SHALL 将选定颜色应用于后续的 Stroke
3. THE Tool_Palette SHALL 显示当前工具的可用粗细选项
4. WHEN 用户选择粗细，THE Annotation_System SHALL 将选定粗细应用于后续的 Stroke
5. THE Annotation_System SHALL 记住每个工具的最后选择的颜色和粗细
6. WHEN 用户切换回之前使用的工具，THE Annotation_System SHALL 恢复该工具的颜色和粗细设置

### Requirement 6: 标注层管理

**User Story:** 作为用户，我希望标注能够正确地覆盖在 PDF 页面上，以便标注与 PDF 内容对齐。

#### Acceptance Criteria

1. THE Annotation_System SHALL 为每个 PDF 页面创建独立的 Annotation_Layer
2. THE Annotation_Layer SHALL 覆盖在对应的 PDF 页面上并保持透明背景
3. WHEN PDF 页面缩放，THE Annotation_Layer SHALL 同步缩放并保持标注与 PDF 内容的相对位置
4. WHEN PDF 页面旋转，THE Annotation_Layer SHALL 同步旋转并保持标注的正确方向
5. WHEN 用户滚动 PDF_Viewer，THE Annotation_System SHALL 仅渲染可见页面的 Annotation_Layer
6. THE Annotation_Layer SHALL 支持单页和双页视图模式

### Requirement 7: 标注数据保存

**User Story:** 作为用户，我想保存我的标注，以便下次打开同一 PDF 文件时能够看到之前的标注。

#### Acceptance Criteria

1. WHEN 用户添加或删除标注，THE Annotation_System SHALL 将 Annotation_Data 标记为已修改
2. WHEN 用户关闭 PDF 文件或退出应用程序，THE Annotation_Storage SHALL 将 Annotation_Data 保存到文件系统
3. THE Annotation_Storage SHALL 将标注数据保存为与 PDF 文件关联的独立 ISF 文件
4. THE Annotation_Storage SHALL 使用 InkStrokeContainer.SaveAsync() API 序列化墨迹笔画
5. WHEN 保存失败，THE Annotation_System SHALL 显示错误消息并保留内存中的 Annotation_Data
6. THE Annotation_Storage SHALL 在 5 秒内完成保存操作

### Requirement 8: 标注数据加载

**User Story:** 作为用户，我想在打开 PDF 文件时自动加载之前保存的标注，以便继续我的工作。

#### Acceptance Criteria

1. WHEN 用户打开 PDF 文件，THE Annotation_Storage SHALL 检查是否存在关联的 ISF 标注数据文件
2. WHEN 标注数据文件存在，THE Annotation_Storage SHALL 使用 InkStrokeContainer.LoadAsync() API 加载 Annotation_Data 到内存
3. WHEN 标注数据加载完成，THE Annotation_System SHALL 在对应的 Annotation_Layer 上渲染所有 Stroke
4. WHEN 标注数据文件不存在，THE Annotation_System SHALL 初始化空的 InkStrokeContainer
5. WHEN 加载失败，THE Annotation_System SHALL 显示警告消息并初始化空的 Annotation_Data
6. THE Annotation_Storage SHALL 在 3 秒内完成加载操作

### Requirement 9: 撤销和重做

**User Story:** 作为用户，我想撤销和重做标注操作，以便纠正错误或恢复意外删除的标注。

#### Acceptance Criteria

1. WHEN 用户添加或删除 Stroke，THE Annotation_System SHALL 将操作记录到 Undo_Stack
2. WHEN 用户点击撤销按钮或使用 Ctrl+Z 快捷键，THE Annotation_System SHALL 从 Undo_Stack 弹出最后一个操作并恢复到操作前的状态
3. WHEN 撤销操作执行，THE Annotation_System SHALL 将撤销的操作记录到 Redo_Stack
4. WHEN 用户点击重做按钮或使用 Ctrl+Y 快捷键，THE Annotation_System SHALL 从 Redo_Stack 弹出最后一个操作并重新执行
5. WHEN 用户执行新的标注操作，THE Annotation_System SHALL 清空 Redo_Stack
6. THE Undo_Stack SHALL 保留至少 50 个操作历史
7. THE 撤销按钮 SHALL 位于工具栏中目录按钮之后、标注工具图标之前
8. THE 重做按钮 SHALL 位于撤销按钮之后、标注工具图标之前
9. WHEN Undo_Stack 为空，THE 撤销按钮 SHALL 显示为禁用状态
10. WHEN Redo_Stack 为空，THE 重做按钮 SHALL 显示为禁用状态

### Requirement 10: 与现有 PDF 查看器集成

**User Story:** 作为用户，我希望标注功能无缝集成到现有的 PDF 查看器中，以便在不影响现有功能的情况下使用标注。

#### Acceptance Criteria

1. THE Annotation_System SHALL 集成到现有的 PdfViewerPage 组件中
2. WHEN 标注工具激活，THE PDF_Viewer SHALL 保持所有现有功能（缩放、滚动、页面跳转、旋转）
3. THE Annotation_System SHALL 使用现有的 IPdfBackend 接口进行 PDF 渲染
4. THE Tool_Palette SHALL 集成到现有的工具栏中
5. WHEN 用户切换页面，THE Annotation_System SHALL 保存当前页面的标注状态并加载目标页面的标注
6. THE Annotation_System SHALL 支持现有的单页和双页视图模式

### Requirement 11: 性能要求

**User Story:** 作为用户，我希望标注功能响应迅速且不影响 PDF 查看器的性能，以便流畅地使用应用程序。

#### Acceptance Criteria

1. WHEN 用户绘制 Stroke，THE Annotation_System SHALL 在 16 毫秒内渲染每一帧（60 FPS）
2. WHEN 用户切换工具，THE Annotation_System SHALL 在 100 毫秒内完成切换
3. WHEN 用户加载包含 100 个 Stroke 的页面，THE Annotation_System SHALL 在 500 毫秒内完成渲染
4. THE Annotation_System SHALL 在内存中缓存当前页面和相邻页面的 Annotation_Data
5. WHEN PDF 文件包含超过 50 页，THE Annotation_System SHALL 仅加载可见页面和相邻页面的标注数据
6. THE Annotation_System SHALL 使用异步操作进行文件 I/O，避免阻塞 UI 线程

### Requirement 12: 输入设备支持

**User Story:** 作为用户，我想使用不同的输入设备进行标注，以便根据我的硬件和使用场景选择最合适的输入方式。

#### Acceptance Criteria

1. THE Annotation_System SHALL 支持 Windows Ink 触控笔输入
2. THE Annotation_System SHALL 支持鼠标输入
3. THE Annotation_System SHALL 支持触摸屏输入
4. WHEN 用户使用触控笔，THE Annotation_System SHALL 检测压感并调整笔画粗细
5. WHEN 用户使用触控笔的橡皮擦端，THE Annotation_System SHALL 自动切换到 Eraser_Tool
6. WHEN 用户使用鼠标或触摸输入，THE Annotation_System SHALL 使用选定的笔触粗细绘制 Stroke

### Requirement 13: 标注数据格式

**User Story:** 作为开发者，我需要定义标注数据的存储格式，以便实现可靠的保存和加载功能。

#### Acceptance Criteria

1. THE Annotation_Storage SHALL 使用 ISF (Ink Serialized Format) 格式存储 Annotation_Data
2. THE Annotation_Storage SHALL 使用 InkStrokeContainer.SaveAsync() API 保存墨迹笔画
3. THE Annotation_Storage SHALL 使用 InkStrokeContainer.LoadAsync() API 加载墨迹笔画
4. THE Annotation_Storage SHALL 为每个 PDF 页面创建独立的 ISF 文件
5. THE Annotation_Storage SHALL 将 PDF 文件标识符和页面索引编码到 ISF 文件名中
6. THE Annotation_Storage SHALL 将标注文件保存到应用程序的本地数据文件夹
7. WHEN 保存标注数据，THE ISF 格式 SHALL 保留所有笔画属性（路径点、颜色、粗细、压感、倾斜角度）

### Requirement 14: 错误处理

**User Story:** 作为用户，我希望应用程序能够优雅地处理错误情况，以便在出现问题时不会丢失数据或崩溃。

#### Acceptance Criteria

1. WHEN 标注数据保存失败，THE Annotation_System SHALL 显示错误消息并保留内存中的数据
2. WHEN 标注数据加载失败，THE Annotation_System SHALL 显示警告消息并初始化空的标注数据
3. WHEN 标注数据文件损坏，THE Annotation_System SHALL 尝试恢复部分数据并记录错误日志
4. WHEN 磁盘空间不足，THE Annotation_System SHALL 显示错误消息并阻止新的标注操作
5. WHEN 用户尝试在只读 PDF 上标注，THE Annotation_System SHALL 允许标注但将数据保存到独立文件
6. THE Annotation_System SHALL 记录所有错误到应用程序日志文件

### Requirement 15: 用户界面

**User Story:** 作为用户，我希望标注工具的界面直观易用，以便快速上手并高效使用。

#### Acceptance Criteria

1. THE Tool_Palette SHALL 使用与 Microsoft Edge 和 Windows InkToolbar 一致的图标和布局
2. THE Tool_Palette SHALL 按以下顺序排列按钮：目录按钮、撤销按钮、重做按钮、荧光笔、硬笔、橡皮擦
3. THE 荧光笔和硬笔按钮 SHALL 在按钮图标上显示当前选中的笔颜色作为视觉指示器
4. WHEN 用户更改笔颜色，THE 按钮图标 SHALL 立即更新以反映新选中的颜色
5. THE 笔按钮 SHALL 使用 InkToolbarPenButton 或等效控件，支持 Palette 和 SelectedBrushIndex 属性
6. WHEN 标注工具按钮被选中，THE Tool_Palette SHALL 使用浅灰色背景或底部边框指示选中状态
7. WHEN 用户点击笔按钮，THE Tool_Palette SHALL 显示颜色调色板和笔触粗细选择器的浮出控件
8. THE 颜色调色板 SHALL 使用 GridView 布局显示可用颜色，每个颜色为圆形色块
9. THE Tool_Palette SHALL 在工具栏中占用不超过 400 像素的水平空间
10. WHEN 用户悬停在工具图标上，THE Tool_Palette SHALL 显示工具提示
11. THE Tool_Palette SHALL 支持浅色和深色主题
12. THE Tool_Palette SHALL 在触摸设备上提供至少 44x44 像素的触摸目标
13. THE 撤销按钮和重做按钮 SHALL 与标注工具图标使用视觉分隔符（如竖线或 8 像素间距）分隔

