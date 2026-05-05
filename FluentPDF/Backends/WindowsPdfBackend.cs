using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Backends
{
    /// <summary>
    /// 基于 Windows.Data.Pdf 的渲染后端（系统内置，零依赖）。
    /// 原 PdfViewerPage 中的渲染逻辑直接迁移至此，Page 层不再持有 PdfDocument。
    /// </summary>
    public sealed class WindowsPdfBackend : IPdfBackend
    {
        private PdfDocument? _doc;

        // Stream 对象池：复用 InMemoryRandomAccessStream，减少大块内存分配/释放
        private readonly Stack<InMemoryRandomAccessStream> _streamPool = new();
        private const int StreamPoolMaxSize = 8;

        // ── IPdfBackend ───────────────────────────────────────────

        public uint PageCount => _doc?.PageCount ?? 0;

        public bool SupportsPassword => false;
        public string BackendName    => "Windows";

        public (double Width, double Height) GetPageSize(uint pageIndex)
        {
            if (_doc == null) throw new InvalidOperationException("文档尚未加载。");
            using var page = _doc.GetPage(pageIndex);
            return (page.Size.Width, page.Size.Height);
        }

        public async Task LoadAsync(StorageFile file, string? password = null, CancellationToken ct = default)
        {
            _doc = null;

            try
            {
                // Windows.Data.Pdf 不支持传密码的重载在部分系统版本上不可靠，
                // 此处仅使用无密码重载，加密文件会触发 0x8007052b。
                _doc = await PdfDocument.LoadFromFileAsync(file).AsTask(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x8007052b))
            {
                throw new InvalidOperationException("该 PDF 文件已加密，Windows.Data.Pdf 后端暂不支持密码保护的文件。");
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80004005))
            {
                throw new InvalidOperationException("文件不是有效的 PDF 文档。");
            }
        }

        public async Task<BitmapImage?> RenderPageAsync(
            uint pageIndex, uint targetWidth, uint targetHeight, CancellationToken ct)
        {
            if (_doc == null) return null;

            try
            {
                using PdfPage page = _doc.GetPage(pageIndex);
                var opts = new PdfPageRenderOptions
                {
                    DestinationWidth  = targetWidth,
                    DestinationHeight = targetHeight,
                };

                var stream = RentStream();
                try
                {
                    await page.RenderToStreamAsync(stream, opts).AsTask(ct);
                    if (ct.IsCancellationRequested) return null;

                    var bitmap = new BitmapImage();
                    stream.Seek(0);
                    await bitmap.SetSourceAsync(stream).AsTask(ct);
                    return ct.IsCancellationRequested ? null : bitmap;
                }
                finally
                {
                    ReturnStream(stream);
                }
            }
            catch (OperationCanceledException) { return null; }
            catch { return null; }
        }

        // ── Stream 池 ─────────────────────────────────────────────

        private InMemoryRandomAccessStream RentStream()
        {
            if (_streamPool.Count > 0)
            {
                var s = _streamPool.Pop();
                s.Seek(0);
                s.Size = 0;
                return s;
            }
            return new InMemoryRandomAccessStream();
        }

        private void ReturnStream(InMemoryRandomAccessStream stream)
        {
            if (_streamPool.Count < StreamPoolMaxSize)
                _streamPool.Push(stream);
            else
                stream.Dispose();
        }

        // ── IDisposable ───────────────────────────────────────────

        public void Dispose()
        {
            _doc = null;  // WinRT PdfDocument 不实现 IDisposable，直接置空让 GC 回收
            while (_streamPool.Count > 0)
                _streamPool.Pop().Dispose();
        }
    }
}
