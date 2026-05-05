// ── PdfiumBackend ─────────────────────────────────────────────────────────────
// PDFium 渲染后端。
//
// 依赖（两个包，缺一不可）：
//   <PackageReference Include="PDFtoImage" Version="5.2.1" />
//   （PDFtoImage 会自动拉取 bblanchon.PDFium，包含 win-x64 + win-arm64 原生 dll）
//
// PDFtoImage 内部用 SkiaSharp 输出 SKBitmap，我们把它编码为 PNG 字节流，
// 再交给 BitmapImage.SetSourceAsync 完成 WinRT 侧的解码。
// 这条路径比 GDI/DC 方式更干净，且对 UWP/WinUI 友好。
//
// 重要限制：PDFium 原生库不是线程安全的。
// PDFtoImage 内部已用全局锁串行化所有 PDFium 调用，
// 因此本后端的 RenderPageAsync 可以并发调用，但实际渲染仍是串行的。
// 如需更高吞吐，需要多进程方案（当前场景不必要）。
//
// 当前状态：骨架已就绪，尚未接入运行时（NuGet 包未添加）。
//   接入步骤：
//   1. 在 FluentPDF.csproj 中添加 PDFtoImage 引用（见上方）
//   2. 在项目属性 → 生成 → 条件编译符号 中添加 PDFIUM_ENABLED
//      或在 .csproj 中：<DefineConstants>$(DefineConstants);PDFIUM_ENABLED</DefineConstants>
//   3. 在 PdfViewerPage 中将后端替换为 new PdfiumBackend()
// ─────────────────────────────────────────────────────────────────────────────

#if PDFIUM_ENABLED

using PDFtoImage;
using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Backends
{
    /// <summary>
    /// 基于 PDFium（via PDFtoImage + SkiaSharp）的渲染后端。
    /// 支持密码保护 PDF，win-x64 和 win-arm64 均有原生二进制。
    ///
    /// 注意：PDFtoImage 每次渲染都会重新解析 PDF（内部无文档缓存），
    /// 并发渲染会导致内存暴涨。用 SemaphoreSlim(1,1) 强制串行，
    /// 牺牲并发换稳定性。后续可换用能持有文档状态的绑定库优化。
    /// </summary>
    public sealed class PdfiumBackend : IPdfBackend
    {
        private byte[]? _docBytes;
        private string? _password;
        private uint    _pageCount;
        private (double Width, double Height)[]? _pageSizes;

        // PDFtoImage 每次渲染都重新解析 PDF，强制串行避免并发解析导致 OOM
        private readonly SemaphoreSlim _renderSem = new(1, 1);

        // ── IPdfBackend ───────────────────────────────────────────

        public uint   PageCount        => _pageCount;
        public bool   SupportsPassword => true;
        public string BackendName      => "PDFium";

        public (double Width, double Height) GetPageSize(uint pageIndex)
        {
            if (_pageSizes == null || pageIndex >= _pageSizes.Length)
                throw new InvalidOperationException("文档尚未加载。");
            return _pageSizes[pageIndex];
        }

        public async Task LoadAsync(StorageFile file, string? password = null, CancellationToken ct = default)
        {
            _docBytes  = null;
            _pageSizes = null;
            _pageCount = 0;
            _password  = password;

            // 读取文件到内存，后续渲染复用同一份字节
            try
            {
                var buffer = await FileIO.ReadBufferAsync(file).AsTask(ct);
                _docBytes = new byte[buffer.Length];
                using var reader = DataReader.FromBuffer(buffer);
                reader.ReadBytes(_docBytes);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法读取文件：{ex.Message}");
            }

            // 在 ThreadPool 线程获取页数和页面尺寸（PDFium 调用会阻塞）
            // 不传 ct 给 Task.Run，避免任务启动前取消时抛出未捕获的 TaskCanceledException
            await Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    using var stream = new MemoryStream(_docBytes);

                    int count = Conversion.GetPageCount(stream, leaveOpen: true, password: _password);
                    _pageCount = (uint)count;

                    stream.Seek(0, SeekOrigin.Begin);
                    var sizes = Conversion.GetPageSizes(stream, leaveOpen: true, password: _password);

                    _pageSizes = new (double, double)[count];
                    for (int i = 0; i < count; i++)
                        _pageSizes[i] = (sizes[i].Width, sizes[i].Height);
                }
                catch (Exception ex) when (
                    ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("密码错误或文件已加密，请输入正确的密码。");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"无法解析 PDF 文件：{ex.Message}");
                }
            });
        }

        public async Task<BitmapImage?> RenderPageAsync(
            uint pageIndex, uint targetWidth, uint targetHeight, CancellationToken ct)
        {
            if (_docBytes == null) return null;
            if (ct.IsCancellationRequested) return null;

            // 串行化渲染：PDFtoImage 每次调用都重新解析 PDF，并发会 OOM
            await _renderSem.WaitAsync(ct).ConfigureAwait(false);
            // WaitAsync 本身不会因 ct 取消而抛出未捕获异常（它返回 cancelled task），
            // 但 await 会把 OperationCanceledException 传播出来，
            // 由 PdfViewerPage 的 catch 处理，这里不需要额外 try。
            try
            {
                byte[]? pngBytes = null;
                try
                {
                    pngBytes = await Task.Run(() =>
                    {
                        if (ct.IsCancellationRequested) return null;

                        using var stream = new MemoryStream(_docBytes);
                        using SKBitmap skBitmap = Conversion.ToImage(
                            stream,
                            (Index)(int)pageIndex,
                            leaveOpen: true,
                            _password,
                            new RenderOptions(
                                Dpi:              72,
                                Width:            (int)targetWidth,
                                Height:           (int)targetHeight,
                                WithAnnotations:  false,
                                WithFormFill:     false,
                                WithAspectRatio:  false,
                                Rotation:         PdfRotation.Rotate0,
                                AntiAliasing:     PdfAntiAliasing.All,
                                BackgroundColor:  SKColors.White,
                                Bounds:           null,
                                UseTiling:        false,
                                DpiRelativeToBounds: false,
                                Grayscale:        false
                            )
                        );

                        if (ct.IsCancellationRequested) return null;

                        using var ms = new MemoryStream();
                        skBitmap.Encode(ms, SKEncodedImageFormat.Png, quality: 100);
                        return ms.ToArray();
                    });
                }
                catch (OperationCanceledException) { return null; }
                catch { return null; }

                if (pngBytes == null || ct.IsCancellationRequested) return null;

                try
                {
                    using var ras = new InMemoryRandomAccessStream();
                    using var writer = new DataWriter(ras);
                    writer.WriteBytes(pngBytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    if (ct.IsCancellationRequested) return null;
                    ras.Seek(0);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(ras);
                    return ct.IsCancellationRequested ? null : bitmap;
                }
                catch (OperationCanceledException) { return null; }
                catch { return null; }
            }
            finally
            {
                _renderSem.Release();
            }
        }

        // ── IDisposable ───────────────────────────────────────────

        public void Dispose()
        {
            _docBytes  = null;
            _pageSizes = null;
            _renderSem.Dispose();
        }
    }
}

#else

// PDFIUM_ENABLED 未定义时编译为空占位，不影响项目构建。
namespace FluentPDF.Backends
{
    /// <summary>
    /// PDFium 后端占位类（未启用）。
    /// 启用步骤见文件顶部注释。
    /// </summary>
    public sealed class PdfiumBackend : IPdfBackend
    {
        public uint   PageCount        => 0;
        public bool   SupportsPassword => true;
        public string BackendName      => "PDFium (未启用)";

        public (double Width, double Height) GetPageSize(uint pageIndex)
            => throw new System.NotSupportedException("PDFium 后端未启用，请参考文件顶部注释完成接入。");

        public System.Threading.Tasks.Task LoadAsync(
            Windows.Storage.StorageFile file,
            string? password = null,
            System.Threading.CancellationToken ct = default)
            => throw new System.NotSupportedException("PDFium 后端未启用，请参考文件顶部注释完成接入。");

        public System.Threading.Tasks.Task<Windows.UI.Xaml.Media.Imaging.BitmapImage?> RenderPageAsync(
            uint pageIndex, uint targetWidth, uint targetHeight,
            System.Threading.CancellationToken ct)
            => throw new System.NotSupportedException("PDFium 后端未启用，请参考文件顶部注释完成接入。");

        public void Dispose() { }
    }
}

#endif
