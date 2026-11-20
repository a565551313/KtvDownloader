namespace KtvDownloader.Services
{
    /// <summary>
    /// 老式单音轨卡拉OK的「原唱/伴奏在哪个声道」规则（docs §7.5，来自上游 VocalSwitchHelper.kt）。
    /// <para>
    /// 为什么单独一个类：这条规则同时被两个完全不同的执行路径用到 ——
    /// </para>
    /// <list type="bullet">
    /// <item><b>下载</b>：<see cref="FfmpegArguments.BuildPanFilter"/> 生成 ffmpeg 的 pan 滤镜；</item>
    /// <item><b>预览</b>：<c>PreviewForm</c> 调 libVLC 的 <c>libvlc_audio_set_channel</c> 实时切声道。</item>
    /// </list>
    /// <para>
    /// 两边各写一份 `accomp == 2` 判断迟早会漂移（一边改了另一边忘改，
    /// 于是"预览里是原唱、下载下来变伴奏"）。收拢到这里，两边都只问它。
    /// </para>
    /// </summary>
    public static class VocalChannelRule
    {
        // libvlc_audio_channel_t（libVLC 3.x）
        /// <summary>立体声（默认，两个声道都放）。</summary>
        public const int ChannelStereo = 1;

        /// <summary>只放左声道。</summary>
        public const int ChannelLeft = 3;

        /// <summary>只放右声道。</summary>
        public const int ChannelRight = 4;

        /// <summary>
        /// 原唱是否在**右**声道。
        /// <para>
        /// <c>accomp == 2</c> → 左声道是伴奏、右声道是原唱；
        /// 其它值（含 <c>1</c> 和"未知"的 <c>0</c>）→ 左声道是原唱、右声道是伴奏。
        /// </para>
        /// </summary>
        public static bool OriginalIsRightChannel(int accomp)
        {
            return accomp == 2;
        }

        /// <summary>
        /// 要听原唱/伴奏时，该让播放器只放哪个声道。
        /// </summary>
        /// <param name="accomp">曲库 songs.accomp 字段。</param>
        /// <param name="wantOriginal">true 要原唱，false 要伴奏。</param>
        /// <returns>libVLC 的声道常量（<see cref="ChannelLeft"/> / <see cref="ChannelRight"/>）。</returns>
        public static int ResolveChannel(int accomp, bool wantOriginal)
        {
            var originalIsRight = OriginalIsRightChannel(accomp);

            if (wantOriginal) return originalIsRight ? ChannelRight : ChannelLeft;
            return originalIsRight ? ChannelLeft : ChannelRight;
        }

        /// <summary>
        /// 要听原唱/伴奏时，ffmpeg 的 pan 滤镜该取哪个输入声道（<c>c0</c> = 左，<c>c1</c> = 右）。
        /// </summary>
        public static string ResolveSourceChannel(int accomp, bool wantOriginal)
        {
            var originalIsRight = OriginalIsRightChannel(accomp);

            if (wantOriginal) return originalIsRight ? "c1" : "c0";
            return originalIsRight ? "c0" : "c1";
        }

        /// <summary>
        /// 这个 accomp 值是否可信。
        /// <para>
        /// <c>0</c> 表示曲库没标注，此时按「左=原唱」处理只是**猜测**（多数老歌确实如此，
        /// 但不是全部）。界面上应该把这一点讲清楚，别让用户以为是确定结论。
        /// </para>
        /// </summary>
        public static bool IsAccompKnown(int accomp)
        {
            return accomp == 1 || accomp == 2;
        }
    }
}
