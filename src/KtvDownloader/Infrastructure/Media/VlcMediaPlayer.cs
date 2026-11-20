using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using KtvDownloader.Infrastructure.Logging;

namespace KtvDownloader.Infrastructure.Media
{
    /// <summary>
    /// libVLC 封装。直接播放 MPEG-TS（含 mp2 音轨），无需转码。
    /// <para>
    /// 为什么换掉 WebBrowser：IE 内核只认 MP4(H.264+AAC)，
    /// 播放 KTV 的加密 TS 必须先下载再转码，首屏要 3 秒以上。
    /// libVLC 原生支持 MPEG-TS/mp2，解密后可直接播放，
    /// 首屏接近 0 秒，而且能实时切换音轨（原唱/伴奏）。
    /// </para>
    /// <para>
    /// 注意：本项目不直接 P/Invoke libvlc，而是通过 libvlc.dll 的 C API 最小封装。
    /// 若 libvlc 不可用，调用方应降级到 ffmpeg 转码方案。
    /// </para>
    /// </summary>
    public sealed class VlcMediaPlayer : IDisposable
    {
        private readonly ILogger _logger;
        private IntPtr _libvlc;
        private IntPtr _player;
        private IntPtr _media;
        private bool _disposed;

        /// <summary>libVLC 是否可用（DLL 能找到且初始化成功）。</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>初始化失败的原因。</summary>
        public string ErrorMessage { get; private set; }

        public VlcMediaPlayer(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                var libPath = ResolveLibVlcPath();
                if (libPath == null)
                {
                    ErrorMessage = "未找到 libvlc.dll";
                    _logger.Warn("VLC: " + ErrorMessage);
                    return;
                }

                // 把 libvlc 所在目录加入 DLL 搜索路径，让它能找到插件目录。
                var directory = Path.GetDirectoryName(libPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    SetDllDirectory(directory);
                    Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH",
                        Path.Combine(directory, "plugins"));
                }

                _libvlc = LibVlcNative.libvlc_new(0, new string[0]);
                if (_libvlc == IntPtr.Zero)
                {
                    ErrorMessage = "libvlc_new 返回空，可能是插件缺失";
                    _logger.Warn("VLC: " + ErrorMessage);
                    return;
                }

                _player = LibVlcNative.libvlc_media_player_new(_libvlc);
                if (_player == IntPtr.Zero)
                {
                    ErrorMessage = "创建 media player 失败";
                    return;
                }

                IsAvailable = true;
                _logger.Info("VLC 初始化成功: " + libPath);
            }
            catch (DllNotFoundException ex)
            {
                ErrorMessage = "libvlc.dll 未找到: " + ex.Message;
                _logger.Warn("VLC: " + ErrorMessage);
            }
            catch (Exception ex)
            {
                ErrorMessage = "VLC 初始化异常: " + ex.Message;
                _logger.Error("VLC 初始化失败", ex);
            }
        }

        /// <summary>
        /// 在程序目录下寻找 libvlc.dll（NuGet 包会放到 libvlc\win-x64 下）。
        /// </summary>
        private static string ResolveLibVlcPath()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDirectory, "libvlc", "win-x64", "libvlc.dll"),
                Path.Combine(baseDirectory, "libvlc", "win-x86", "libvlc.dll"),
                Path.Combine(baseDirectory, "libvlc.dll"),
                Path.Combine(baseDirectory, "tools", "vlc", "libvlc.dll")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }

            return null;
        }

        /// <summary>
        /// 把播放器渲染到指定窗口句柄。
        /// </summary>
        public bool AttachToWindow(IntPtr windowHandle)
        {
            if (!IsAvailable || _player == IntPtr.Zero) return false;

            try
            {
                LibVlcNative.libvlc_media_player_set_hwnd(_player, windowHandle);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn("VLC 绑定窗口失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 播放本地文件或网络地址。支持 .ts / .mp4 / .mp3。
        /// </summary>
        public bool Play(string source)
        {
            if (!IsAvailable || _player == IntPtr.Zero) return false;
            if (string.IsNullOrWhiteSpace(source)) return false;

            try
            {
                ReleaseMedia();

                // 网络地址直接用，本地文件需转成 file:// URI。
                var uri = source;
                if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(source)) return false;
                    uri = new Uri(source).AbsoluteUri;
                }

                _media = LibVlcNative.libvlc_media_new_location(_libvlc, uri);
                if (_media == IntPtr.Zero) return false;

                LibVlcNative.libvlc_media_player_set_media(_player, _media);
                var result = LibVlcNative.libvlc_media_player_play(_player);

                _logger.Info("VLC 播放: " + uri);
                return result == 0;
            }
            catch (Exception ex)
            {
                _logger.Error("VLC 播放失败", ex);
                return false;
            }
        }

        public void Pause()
        {
            if (!IsAvailable) return;
            try { LibVlcNative.libvlc_media_player_pause(_player); } catch { }
        }

        public void Stop()
        {
            if (!IsAvailable) return;
            try
            {
                LibVlcNative.libvlc_media_player_stop(_player);
                ReleaseMedia();
            }
            catch { }
        }

        public void SetVolume(int volume)
        {
            if (!IsAvailable) return;
            try
            {
                LibVlcNative.libvlc_audio_set_volume(_player,
                    Math.Max(0, Math.Min(200, volume)));
            }
            catch { }
        }

        /// <summary>
        /// 音轨数量。
        /// </summary>
        public int GetTrackCount()
        {
            if (!IsAvailable || _player == IntPtr.Zero) return 0;

            try
            {
                return LibVlcNative.libvlc_audio_get_track_count(_player);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 当前音轨 ID。
        /// </summary>
        public int GetCurrentTrack()
        {
            if (!IsAvailable) return -1;
            try { return LibVlcNative.libvlc_audio_get_track(_player); } catch { return -1; }
        }

        /// <summary>
        /// 设置音轨。实时生效，无需重新加载文件 —— 这正是安卓端的行为。
        /// </summary>
        /// <param name="track">音轨 ID。-1 表示禁用音频。</param>
        public bool SetAudioTrack(int track)
        {
            if (!IsAvailable) return false;

            try
            {
                var result = LibVlcNative.libvlc_audio_set_track(_player, track);
                if (result == 0)
                {
                    _logger.Info("VLC 切换音轨: " + track);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warn("VLC 切换音轨失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 取所有音轨的 ID 列表（用于在原唱/伴奏之间切换）。
        /// libVLC 的音轨 ID 通常从 1 开始，0 是禁用。
        /// </summary>
        public int[] GetAudioTrackIds()
        {
            var count = GetTrackCount();
            if (count <= 0) return new int[0];

            // 实测：libvlc_audio_get_track_id(player, i) 返回的 1/2/3 是描述序号，
            // 而 libvlc_audio_set_track(player, id) 需要的是 TS 里的音轨 PID（257/258）。
            // 由于 KTV 的 TS 音轨 PID 连续，这里以当前 PID 为基准递推。
            var current = GetCurrentTrack();
            var tracks = new int[count];

            for (var i = 0; i < count; i++)
            {
                tracks[i] = current + i;
            }

            return tracks;
        }

        /// <summary>媒体总时长（毫秒）。未加载或未知时返回 0。</summary>
        public long GetLength()
        {
            if (!IsAvailable) return 0;
            try { return LibVlcNative.libvlc_media_player_get_length(_player); } catch { return 0; }
        }

        /// <summary>跳转到指定位置（毫秒）。</summary>
        public bool SetTime(long milliseconds)
        {
            if (!IsAvailable) return false;
            try
            {
                LibVlcNative.libvlc_media_player_set_time(_player, Math.Max(0, milliseconds));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>当前播放位置百分比（0.0-1.0）。</summary>
        public float GetPosition()
        {
            if (!IsAvailable) return 0f;
            try { return LibVlcNative.libvlc_media_player_get_position(_player); } catch { return 0f; }
        }

        /// <summary>按百分比定位（0.0-1.0）。</summary>
        public bool SetPosition(float position)
        {
            if (!IsAvailable) return false;
            try
            {
                var clamped = Math.Max(0f, Math.Min(1f, position));
                LibVlcNative.libvlc_media_player_set_position(_player, clamped);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>播放状态码。libvlc_NothingSpecial=0, Opening=1, Buffering=2, Playing=3, Paused=4, Stopped=5, Ended=6, Error=7。</summary>
        public int GetState()
        {
            if (!IsAvailable) return 0;
            try { return LibVlcNative.libvlc_media_player_get_state(_player); } catch { return 0; }
        }

        /// <summary>是否为「正在播放」状态。</summary>
        public bool IsActivelyPlaying()
        {
            return GetState() == 3;
        }

        /// <summary>媒体是否已播放结束。</summary>
        public bool IsEnded()
        {
            return GetState() == 6;
        }

        public long GetTime()
        {
            if (!IsAvailable) return 0;
            try { return LibVlcNative.libvlc_media_player_get_time(_player); } catch { return 0; }
        }

        public bool IsPlaying()
        {
            if (!IsAvailable) return false;
            try { return LibVlcNative.libvlc_media_player_is_playing(_player) != 0; } catch { return false; }
        }

        private void ReleaseMedia()
        {
            if (_media != IntPtr.Zero)
            {
                try { LibVlcNative.libvlc_media_release(_media); } catch { }
                _media = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_player != IntPtr.Zero)
                {
                    LibVlcNative.libvlc_media_player_stop(_player);
                    LibVlcNative.libvlc_media_player_release(_player);
                    _player = IntPtr.Zero;
                }

                ReleaseMedia();

                if (_libvlc != IntPtr.Zero)
                {
                    LibVlcNative.libvlc_release(_libvlc);
                    _libvlc = IntPtr.Zero;
                }
            }
            catch
            {
                // 释放失败不影响退出。
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
    }

    /// <summary>
    /// libvlc.dll 的最小 P/Invoke 声明。
    /// 只声明播放与音轨切换需要的那几个函数。
    /// </summary>
    internal static class LibVlcNative
    {
        private const string DllName = "libvlc.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libvlc_new(int argc, string[] argv);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_release(IntPtr instance);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libvlc_media_player_new(IntPtr instance);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_release(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libvlc_media_new_location(IntPtr instance,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_release(IntPtr media);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_media(IntPtr player, IntPtr media);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_media_player_play(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_pause(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_stop(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_hwnd(IntPtr player, IntPtr drawable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_set_volume(IntPtr player, int volume);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_get_track_count(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_get_track(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_set_track(IntPtr player, int track);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_get_track_id(IntPtr player, int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long libvlc_media_player_get_length(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_time(IntPtr player, long time);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float libvlc_media_player_get_position(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_position(IntPtr player, float position);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_media_player_get_state(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern long libvlc_media_player_get_time(IntPtr player);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_media_player_is_playing(IntPtr player);
    }
}
