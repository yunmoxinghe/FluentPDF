using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace FluentPDF.Backends
{
    /// <summary>
    /// PDF 渲染后端抽象。
    /// 实现类负责文档加载和页面位图渲染，上层的缓存、LRU、滚动预测逻辑不感知具体引擎。
    /// </summary>
    public interface IPdfBackend : IDisposable
    {
        // ── 文档信息 ──────────────────────────────────────────────

        /// <summary>总页数。LoadAsync 成功后有效。</summary>
        uint PageCount { get; }

        /// <summary>返回指定页的原始尺寸（点/逻辑像素，与引擎坐标系一致）。</summary>
        (double Width, double Height) GetPageSize(uint pageIndex);

        // ── 加载 ──────────────────────────────────────────────────

        /// <summary>
        /// 异步加载文件。
        /// 加密文件传入 password；不需要密码时传 null。
        /// 实现类应在此方法内完成文档打开，但不必预渲染任何页面。
        /// </summary>
        /// <exception cref="InvalidOperationException">文件无效或密码错误时抛出，消息可直接展示给用户。</exception>
        Task LoadAsync(StorageFile file, string? password = null, CancellationToken ct = default);

        // ── 渲染 ──────────────────────────────────────────────────

        /// <summary>
        /// 将指定页渲染为 BitmapImage。
        /// </summary>
        /// <param name="pageIndex">页码（0-based）。</param>
        /// <param name="targetWidth">目标宽度（物理像素）。</param>
        /// <param name="targetHeight">目标高度（物理像素）。</param>
        /// <param name="ct">取消令牌。取消时返回 null，不抛异常。</param>
        /// <returns>渲染结果；取消或出错时返回 null。</returns>
        Task<BitmapImage?> RenderPageAsync(uint pageIndex, uint targetWidth, uint targetHeight, CancellationToken ct);

        // ── 能力查询 ──────────────────────────────────────────────

        /// <summary>是否支持密码保护的 PDF。</summary>
        bool SupportsPassword { get; }

        /// <summary>后端名称，用于调试和设置界面展示。</summary>
        string BackendName { get; }
    }
}
