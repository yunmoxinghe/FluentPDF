namespace FluentPDF.Pages
{
    /// <summary>
    /// 渲染性能档位。所有字段在构造后不可变（init-only），可安全跨线程读取快照。
    /// </summary>
    public sealed class RenderProfile
    {
        public string Name { get; init; } = "";
        public double ResolutionScaleSlow { get; init; }    // 慢速/静止时的分辨率倍率
        public double ResolutionScaleFast { get; init; }    // 快速滚动时的分辨率倍率
        public int    PreloadAhead        { get; init; }    // 滚动方向前方预加载页数
        public int    PreloadBehind       { get; init; }    // 滚动方向后方预加载页数
        public int    BatchSize           { get; init; }    // 并行渲染批量大小
        public int    CacheCapacity       { get; init; }    // Layer2 LRU 缓存容量
        public int    Layer1CacheCapacity { get; init; }    // Layer1 缩略图缓存容量
        public uint   MaxRenderDim        { get; init; }    // 单边最大渲染像素
        public double MaxZoom             { get; init; }    // 最大缩放倍率

        public static readonly RenderProfile Normal = new()
        {
            Name                 = "标准",
            ResolutionScaleSlow  = 1.0,
            ResolutionScaleFast  = 0.5,
            PreloadAhead         = 4,
            PreloadBehind        = 2,
            BatchSize            = 2,
            CacheCapacity        = 60,
            Layer1CacheCapacity  = 200,
            MaxRenderDim         = 3000,
            MaxZoom              = 4.0,
        };

        public static readonly RenderProfile LowEnd = new()
        {
            Name                 = "流畅（低性能设备）",
            ResolutionScaleSlow  = 1.0,   // 停止后渲染全分辨率
            ResolutionScaleFast  = 0.5,   // 快速滚动 0.5x
            PreloadAhead         = 1,     // 只预加载滚动方向下一页
            PreloadBehind        = 0,     // 反方向不预加载
            BatchSize            = 1,     // 单任务渲染，避免并发卡死
            CacheCapacity        = 20,    // 缓存砍到 20 页
            Layer1CacheCapacity  = 60,    // 低端设备缩略图缓存也收窄
            MaxRenderDim         = 1200,  // 最大 1200px，防止炸
            MaxZoom              = 2.0,   // 最大缩放 2x
        };
    }
}
