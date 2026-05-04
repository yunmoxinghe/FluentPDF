# FluentPDF

基于 UWP + .NET 10 Native AOT 的 PDF 阅读器，使用 `Windows.Data.Pdf` 原生渲染，支持触控、笔迹批注和捏合缩放。做触控体验最好的阅读器。

## 功能

- 打开本地 PDF 文件（文件选择器 或 双击文件关联）
- 按需渲染：打开即显示占位框，滚动到哪渲染到哪，大文件不卡顿
- 捏合缩放（25% ~ 400%）
- 每页叠加 `InkCanvas`，支持 Windows Ink 笔迹批注
- `InkToolbar` 提供笔 / 荧光笔 / 橡皮工具
- 密码保护 PDF 友好提示

## 技术栈

| 项目 | 说明 |
|---|---|
| `Windows.Data.Pdf` | PDF 页面渲染为位图 |
| `InkCanvas` + `InkToolbar` | 系统级低延迟笔迹 |
| `ScrollViewer` | 垂直滚动 + 缩放 |
| .NET 10 Native AOT | 快速启动，无 JIT 开销 |

## 构建要求

- Visual Studio 2026
- Windows SDK 10.0.26100.0
- Universal Windows Platform 工具负载

## 已知限制

- 暂不支持密码保护的 PDF
- `InkToolbar` 当前绑定第一页，跨页批注保存待实现
