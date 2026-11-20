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
