using System;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Models;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// 媒体处理服务。封装 ffmpeg，用于 TS 解密后的转码与音轨分离。
    /// </summary>
    public interface IMediaService
    {
        /// <summary>
        /// 更新 ffmpeg 路径，下一次调用时重新解析。
        /// 用于「设置保存后立即生效」，避免为此重建整套服务（那会取消正在下载的任务）。
        /// </summary>
        void UpdateFfmpegPath(string configuredFfmpegPath);

        /// <summary>ffmpeg 是否可用（已找到且能执行）。</summary>
        bool IsFfmpegAvailable { get; }

        /// <summary>当前使用的 ffmpeg 路径，未找到时为 null。</summary>
        string FfmpegPath { get; }

        /// <summary>数出文件里的音频轨数量。</summary>
        Task<int> CountAudioTracksAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 探测媒体信息（音轨数、分辨率、时长），三者来自同一次 <c>ffmpeg -i</c>，结果按路径缓存。
        /// <para>烧录叠加层要靠它拿分辨率算坐标、拿时长算进度。</para>
        /// </summary>
        Task<MediaProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 烧录叠加层（水印图片 / 文字卡片）并输出 MP4。
        /// <para>
        /// ⚠️ 与 <see cref="ConvertToMp4Async"/> 的代价不是一个量级：那边视频流拷贝、几秒完事，
        /// 这边必须整首重编码，一首歌一两分钟起步。哪怕叠加只出现 5 秒也一样 ——
        /// H.264 的帧间预测决定了每一帧都得解码再编码。
        /// </para>
        /// <para>
        /// 没有任何可渲染元素时返回 <c>NothingToBurn = true</c>（<b>不是失败</b>），
        /// 调用方应改走普通转码，别把这种情况报成下载失败。
        /// </para>
        /// </summary>
        /// <param name="progress">0–100 百分比进度。</param>
        /// <param name="sampleSeconds">&gt;0 时只烧前 N 秒（「试烧样片」），0 = 完整烧录。</param>
        Task<BurnInResult> BurnInAsync(
            string source,
            string output,
            OverlayComposition composition,
            VocalOption vocal,
            int audioBitrateKbps,
            IProgress<double> progress,
            int sampleSeconds = 0,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 把 TS 转为 MP4。
        /// <para>
        /// 视频流原样拷贝（零画质损失），音频必须重编码为 AAC：
        /// TS 内音轨是 mp2，直接 copy 塞进 MP4 会被标成 mp3，
        /// 多数播放器解不出，表现为「画面正常但完全没声音」。
        /// </para>
        /// </summary>
        Task<bool> ConvertToMp4Async(
            string sourceTs,
            string outputMp4,
            VocalOption vocal,
            int audioBitrateKbps,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 分离原唱 / 伴奏，产出独立文件。
        /// <para>
        /// 双音轨时按轨道拆（第 1 条=原唱，第 2 条=伴奏）；
        /// 单音轨时按老式卡拉OK的左右声道拆，此时**必须**知道
        /// <paramref name="accomp"/>：1=左原唱右伴奏，2=左伴奏右原唱（docs §7.5）。
        /// 忽略 accomp 会让约一半的老歌把原唱和伴奏搞反。
        /// </para>
        /// </summary>
        /// <param name="accomp">曲库 songs.accomp 字段，1 或 2；未知时传 0 按 1 处理。</param>
        Task<SplitResult> SplitVocalAsync(
            string sourceTs,
            string outputDirectory,
            string baseFileName,
            VocalOption option,
            int audioBitrateKbps,
            int accomp,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 实测本机可用的最佳视频编码器，结果缓存（一个进程只测一次）。
        /// <para>
        /// 按 <c>h264_nvenc</c> → <c>h264_qsv</c> → <c>h264_amf</c> → <c>h264_mf</c>
        /// 的顺序逐个<b>真跑一次微编码</b>，第一个通过的就是答案；全都不通过返回
        /// <c>libx264</c>。ffmpeg 不可用时也返回 <c>libx264</c>。
        /// </para>
        /// <para>
        /// 不能只看 <c>ffmpeg -encoders</c> 的清单：那只说明这个 ffmpeg 构建<b>编译进了</b>
        /// 该编码器，不代表机器上有对应显卡、驱动够新、或者 nvenc 的并发会话数没被占满
        /// （消费级卡有上限）。这些情况下 ffmpeg 会在初始化编码器那一刻失败退出，
        /// 用户看到的只是"下载失败"。
        /// </para>
        /// </summary>
        /// <returns>可直接传给 <c>FfmpegArguments.BuildVideoEncoderOptions</c> 的编码器名。</returns>
        string SuggestVideoEncoder();

        /// <summary>
        /// <see cref="SuggestVideoEncoder"/> 的异步版。
        /// <para>界面必须用这个：一次实测要给每个候选跑一遍 64x64 微编码，
        /// 几百毫秒起步，放在 UI 线程上打开对话框就会卡一下。</para>
        /// </summary>
        Task<string> SuggestVideoEncoderAsync();

        /// <summary>
        /// 把<b>已经烧好叠加层的视频流</b>与另一个文件的音轨合到一起，两边都流拷贝。
        /// <para>
        /// 给 <c>VocalOption.SplitBoth</c> 用：原唱版和伴奏版的画面完全相同
        /// （两个抽取路径都是 <c>-c:v copy</c>），各烧一遍等于把<b>同一段视频重编码两次</b>
        /// —— 一首 4 分钟的歌从 1–3 分钟变成 2–6 分钟，画质还多损失一代。
        /// 只烧第一个、剩下的用这个函数拷视频流，秒级完成，
        /// 而且两个成品的画面逐比特相同，不会出现"原唱版的水印比伴奏版糊一点"。
        /// </para>
        /// <para>
        /// ⚠️ 前提是两边视频流确实同源。调用方只在同一次 <c>SplitVocal</c>
        /// 产出的文件之间这么做，不要拿去合并任意两个视频。
        /// 参数细节见 <c>FfmpegArguments.BuildVideoReuseArguments</c>。
        /// </para>
        /// </summary>
        /// <param name="burnedVideoSource">已烧好叠加层的文件，只取它的视频流。</param>
        /// <param name="audioSource">提供音轨的文件，只取它的第一条音轨。</param>
        /// <returns>成功产出 <paramref name="output"/> 返回 true。</returns>
        Task<bool> ReuseBurnedVideoAsync(
            string burnedVideoSource,
            string audioSource,
            string output,
            CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>
    /// 叠加层烧录结果。
    /// </summary>
    public sealed class BurnInResult
    {
        /// <summary>是否成功产出文件。</summary>
        public bool Success { get; set; }

        /// <summary>产出的文件路径，成功时非空。</summary>
        public string OutputPath { get; set; }

        /// <summary>失败原因；<see cref="NothingToBurn"/> 为 true 时是"跳过的原因"。</summary>
        public string Error { get; set; }

        /// <summary>
        /// 没有可渲染的元素。<b>这不是错误</b> —— 调用方应改走普通转码路径。
        /// 和 <see cref="Success"/> = false 一起出现，靠这个字段区分"该跳过"和"真失败"。
        /// </summary>
        public bool NothingToBurn { get; set; }

        /// <summary>
        /// 非致命警告：字体兜底、.ttc 风险、某个图片读不到尺寸被跳过、编码器回落等。
        /// 这些都不影响产物，但用户该知道（尤其是字体被换掉 —— 成品字形和他选的不一样）。
        /// </summary>
        public System.Collections.Generic.List<string> Warnings { get; private set; }

        public BurnInResult()
        {
            Warnings = new System.Collections.Generic.List<string>();
        }
    }

    /// <summary>
    /// 音轨分离结果。
    /// </summary>
    public sealed class SplitResult
    {
        /// <summary>原唱文件路径，未产出时为 null。</summary>
        public string OriginalPath { get; set; }

        /// <summary>伴奏文件路径，未产出时为 null。</summary>
        public string AccompanimentPath { get; set; }

        /// <summary>处理的音轨数量。</summary>
        public int AudioTrackCount { get; set; }

        public bool HasAny => OriginalPath != null || AccompanimentPath != null;

        /// <summary>所有产出文件的路径集合。</summary>
        public string[] GetAllPaths()
        {
            var list = new System.Collections.Generic.List<string>(2);
            if (OriginalPath != null) list.Add(OriginalPath);
            if (AccompanimentPath != null) list.Add(AccompanimentPath);
            return list.ToArray();
        }
    }
}
