using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Input.Inking;

namespace FluentPDF.Annotations;

/// <summary>
/// 标注存储类，管理标注数据的持久化
/// </summary>
public sealed class AnnotationStorage
{
    private readonly string _pdfId;
    private const string AnnotationsFolderName = "Annotations";

    /// <summary>
    /// 构造函数，初始化标注存储
    /// </summary>
    /// <param name="pdfId">PDF 文件的唯一标识符</param>
    public AnnotationStorage(string pdfId)
    {
        if (string.IsNullOrWhiteSpace(pdfId))
            throw new ArgumentException("PDF ID cannot be null or empty", nameof(pdfId));

        _pdfId = pdfId;
    }

    /// <summary>
    /// 获取标注文件夹路径
    /// </summary>
    /// <returns>标注文件夹的相对路径</returns>
    public string GetAnnotationFolderPath()
    {
        return $"{AnnotationsFolderName}\\{_pdfId}";
    }

    /// <summary>
    /// 获取页面标注文件路径
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <returns>页面标注文件的相对路径</returns>
    public string GetPageAnnotationFilePath(uint pageIndex)
    {
        return $"{GetAnnotationFolderPath()}\\page_{pageIndex}.isf";
    }

    /// <summary>
    /// 生成 PDF 文件的唯一标识符
    /// </summary>
    /// <param name="pdfFile">PDF 文件</param>
    /// <returns>基于文件路径和创建时间的 SHA256 哈希值</returns>
    public static string GeneratePdfId(StorageFile pdfFile)
    {
        if (pdfFile == null)
            throw new ArgumentNullException(nameof(pdfFile));

        // 使用文件路径和创建时间生成唯一标识符
        string input = $"{pdfFile.Path}_{pdfFile.DateCreated.Ticks}";
        
        using (var sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// 保存页面标注数据
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <param name="container">墨迹笔画容器</param>
    /// <returns>异步任务</returns>
    public async Task SavePageAsync(uint pageIndex, InkStrokeContainer container)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));

        try
        {
            // 检查磁盘空间（至少需要 10 MB）
            var localFolder = ApplicationData.Current.LocalFolder;
            var properties = await localFolder.Properties.RetrievePropertiesAsync(new[] { "System.FreeSpace" });
            if (properties.TryGetValue("System.FreeSpace", out var freeSpaceObj) && freeSpaceObj is ulong freeSpace)
            {
                const ulong MinRequiredSpace = 10 * 1024 * 1024; // 10 MB
                if (freeSpace < MinRequiredSpace)
                {
                    var ex = new IOException($"磁盘空间不足。可用空间: {freeSpace / 1024 / 1024} MB，需要至少 10 MB。");
                    await Helpers.ErrorLogger.LogErrorAsync($"保存页面 {pageIndex} 标注失败：磁盘空间不足", ex);
                    throw ex;
                }
            }

            // 获取或创建标注文件夹
            StorageFolder folder = await GetOrCreateAnnotationFolderAsync();

            // 创建或替换标注文件
            string fileName = $"page_{pageIndex}.isf";
            StorageFile file = await folder.CreateFileAsync(
                fileName,
                CreationCollisionOption.ReplaceExisting);

            // 序列化并保存墨迹笔画
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                await container.SaveAsync(stream);
            }

            await Helpers.ErrorLogger.LogInfoAsync($"成功保存页面 {pageIndex} 的标注数据");
        }
        catch (UnauthorizedAccessException ex)
        {
            await Helpers.ErrorLogger.LogErrorAsync($"保存页面 {pageIndex} 标注失败：权限不足", ex);
            throw new InvalidOperationException($"无法保存页面 {pageIndex} 的标注：权限不足", ex);
        }
        catch (IOException ex)
        {
            await Helpers.ErrorLogger.LogErrorAsync($"保存页面 {pageIndex} 标注失败：磁盘 I/O 错误", ex);
            throw new InvalidOperationException($"无法保存页面 {pageIndex} 的标注：{ex.Message}", ex);
        }
        catch (Exception ex)
        {
            await Helpers.ErrorLogger.LogErrorAsync($"保存页面 {pageIndex} 标注失败：未知错误", ex);
            throw new InvalidOperationException($"无法保存页面 {pageIndex} 的标注", ex);
        }
    }

    /// <summary>
    /// 保存所有页面的标注数据
    /// </summary>
    /// <param name="pages">页面标注数据集合</param>
    /// <returns>异步任务</returns>
    public async Task SaveAllAsync(IEnumerable<PageAnnotationData> pages)
    {
        if (pages == null)
            throw new ArgumentNullException(nameof(pages));

        foreach (var page in pages)
        {
            if (page.IsModified)
            {
                await SavePageAsync(page.PageIndex, page.StrokeContainer);
                page.IsModified = false;
            }
        }
    }

    /// <summary>
    /// 加载页面标注数据
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <returns>墨迹笔画容器，如果文件不存在则返回 null</returns>
    public async Task<InkStrokeContainer?> LoadPageAsync(uint pageIndex)
    {
        try
        {
            // 获取标注文件夹
            StorageFolder folder = await GetAnnotationFolderAsync();
            if (folder == null)
                return null;

            // 获取标注文件
            string fileName = $"page_{pageIndex}.isf";
            StorageFile file = await folder.GetFileAsync(fileName);

            // 反序列化墨迹笔画
            var container = new InkStrokeContainer();
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
            {
                await container.LoadAsync(stream);
            }

            await Helpers.ErrorLogger.LogInfoAsync($"成功加载页面 {pageIndex} 的标注数据");
            return container;
        }
        catch (System.IO.FileNotFoundException)
        {
            // 文件不存在是正常情况，返回 null
            return null;
        }
        catch (Exception ex) when (ex.Message.Contains("corrupt") || ex.Message.Contains("invalid") || ex is System.Runtime.InteropServices.COMException)
        {
            // 文件损坏：尝试恢复或返回空容器
            await Helpers.ErrorLogger.LogErrorAsync($"页面 {pageIndex} 的标注文件已损坏，将初始化空标注", ex);
            
            // 尝试删除损坏的文件
            try
            {
                StorageFolder folder = await GetAnnotationFolderAsync();
                if (folder != null)
                {
                    string fileName = $"page_{pageIndex}.isf";
                    StorageFile file = await folder.GetFileAsync(fileName);
                    await file.DeleteAsync();
                    await Helpers.ErrorLogger.LogWarningAsync($"已删除页面 {pageIndex} 的损坏标注文件");
                }
            }
            catch
            {
                // 删除失败时忽略
            }

            // 返回空容器
            return new InkStrokeContainer();
        }
        catch (Exception ex)
        {
            // 其他错误
            await Helpers.ErrorLogger.LogErrorAsync($"加载页面 {pageIndex} 标注失败：未知错误", ex);
            throw new InvalidOperationException($"无法加载页面 {pageIndex} 的标注", ex);
        }
    }

    /// <summary>
    /// 检查页面是否有标注数据
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <returns>如果存在标注文件则返回 true，否则返回 false</returns>
    public async Task<bool> HasAnnotationsAsync(uint pageIndex)
    {
        try
        {
            StorageFolder folder = await GetAnnotationFolderAsync();
            if (folder == null)
                return false;

            string fileName = $"page_{pageIndex}.isf";
            await folder.GetFileAsync(fileName);
            return true;
        }
        catch (System.IO.FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 删除页面标注数据
    /// </summary>
    /// <param name="pageIndex">页面索引</param>
    /// <returns>异步任务</returns>
    public async Task DeletePageAsync(uint pageIndex)
    {
        try
        {
            StorageFolder folder = await GetAnnotationFolderAsync();
            if (folder == null)
                return;

            string fileName = $"page_{pageIndex}.isf";
            StorageFile file = await folder.GetFileAsync(fileName);
            await file.DeleteAsync();
        }
        catch (System.IO.FileNotFoundException)
        {
            // 文件不存在，无需删除
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to delete annotations for page {pageIndex}", ex);
        }
    }

    /// <summary>
    /// 删除所有标注数据
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task DeleteAllAsync()
    {
        try
        {
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFolder annotationsFolder = await localFolder.GetFolderAsync(AnnotationsFolderName);
            StorageFolder pdfFolder = await annotationsFolder.GetFolderAsync(_pdfId);
            await pdfFolder.DeleteAsync();
        }
        catch (System.IO.FileNotFoundException)
        {
            // 文件夹不存在，无需删除
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to delete all annotations", ex);
        }
    }

    /// <summary>
    /// 获取或创建标注文件夹
    /// </summary>
    /// <returns>标注文件夹</returns>
    private async Task<StorageFolder> GetOrCreateAnnotationFolderAsync()
    {
        StorageFolder localFolder = ApplicationData.Current.LocalFolder;

        // 创建或获取 Annotations 文件夹
        StorageFolder annotationsFolder = await localFolder.CreateFolderAsync(
            AnnotationsFolderName,
            CreationCollisionOption.OpenIfExists);

        // 创建或获取 PDF 特定的文件夹
        StorageFolder pdfFolder = await annotationsFolder.CreateFolderAsync(
            _pdfId,
            CreationCollisionOption.OpenIfExists);

        return pdfFolder;
    }

    /// <summary>
    /// 获取标注文件夹（如果不存在则返回 null）
    /// </summary>
    /// <returns>标注文件夹，如果不存在则返回 null</returns>
    private async Task<StorageFolder?> GetAnnotationFolderAsync()
    {
        try
        {
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFolder annotationsFolder = await localFolder.GetFolderAsync(AnnotationsFolderName);
            StorageFolder pdfFolder = await annotationsFolder.GetFolderAsync(_pdfId);
            return pdfFolder;
        }
        catch (System.IO.FileNotFoundException)
        {
            return null;
        }
    }
}
