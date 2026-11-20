using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Http;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 歌曲取址服务。还原官方 App 的 i.php + do.php 协议。
    /// <para>
    /// 协议要点（任何一处不同都会导致 403 或验签失败）：
    /// 1. 参数顺序固定，sign = MD5(整串参数 + 密钥)，不是按键排序。
    /// 2. 只发 Host 与 Accept: */*，带 UA 会被风控拦截。
    /// 3. ls 只试 1，其它值会被直接拒绝。
    /// 4. 403 通常是限流而非 token 过期，同 token 短退避重试即可。
    /// </para>
    /// <para>
    /// appid / 密钥 / 设备号 / 源站列表都在 <see cref="ProtocolCredentials"/>，
    /// 可以用 <c>%AppData%\KtvDownloader\credentials.json</c> 覆盖，无需改代码重编译。
    /// </para>
    /// </summary>
    public sealed class SongUrlService : ISongUrlService
    {
        private const int RequestTimeoutMs = 10000;
        private const int BlockedRetryDelayMs = 3000;

        private readonly ILogger _logger;
        private readonly ICleanHttpClient _httpClient;
        private readonly object _orderLock = new object();

        /// <summary>源站优先级，运行时可变：失败的沉到队尾，成功的提到队首。</summary>
        private readonly List<string> _hostOrder;

        /// <summary>每个源各自的 token 缓存。</summary>
        private readonly Dictionary<string, string> _tokens =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public SongUrlService(ILogger logger, ICleanHttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            _hostOrder = new List<string>(ProtocolCredentials.Hosts);

            if (_hostOrder.Count == 0)
                throw new InvalidOperationException("源站列表为空，请检查 credentials.json 的 Hosts 配置");

            _logger.Info("取址源站 " + _hostOrder.Count + " 个，首选 " + _hostOrder[0]);
        }

        /// <summary>当前源站优先级快照（诊断用）。</summary>
        public IReadOnlyList<string> GetHostOrder()
        {
            lock (_orderLock)
            {
                return new List<string>(_hostOrder);
            }
        }

        public void ResetSession()
        {
            lock (_orderLock)
            {
                _tokens.Clear();
            }
            _logger.Info("取址会话已重置");
        }

        public async Task<SongUrlResult> ResolveAsync(
            string musicNo,
            string resolution = "720",
            bool preferH265 = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(musicNo))
                return SongUrlResult.Fail("歌号为空");

            List<string> hosts;
            lock (_orderLock)
            {
                hosts = new List<string>(_hostOrder);
            }

            var lastError = "所有源均不可用";

            foreach (var host in hosts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var token = await GetTokenAsync(host, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(token))
                    {
                        _logger.Warn("取 token 失败，降级源: " + host);
                        Demote(host);
                        lastError = "取 token 失败: " + host;
                        continue;
                    }

                    var url = await FetchSongUrlAsync(
                        host, musicNo, token, resolution, preferH265, cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(url))
                    {
                        Promote(host);
                        _logger.Info("取址成功 歌号=" + musicNo + " 源=" + host);
                        return SongUrlResult.Ok(url, host);
                    }

                    _logger.Warn("取地址失败，降级源: " + host);
                    Demote(host);
                    lastError = "取地址失败: " + host;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn("取址异常 源=" + host + " : " + ex.GetType().Name + " " + ex.Message);
                    Demote(host);
                    lastError = ex.Message;
                }
            }

            _logger.Error("取址全部失败 歌号=" + musicNo + " 原因=" + lastError);
            return SongUrlResult.Fail(lastError);
        }

        #region 协议实现

        private async Task<string> GetTokenAsync(string host, CancellationToken cancellationToken)
        {
            lock (_orderLock)
            {
                string cached;
                if (_tokens.TryGetValue(host, out cached) && !string.IsNullOrEmpty(cached))
                    return cached;
            }

            var timestamp = GetUnixTimestamp();
            var parameters = "appid=" + ProtocolCredentials.AppId
                             + "&mac=" + ProtocolCredentials.DeviceMac + "_" + ProtocolCredentials.DeviceSn
                             + "&sn=" + ProtocolCredentials.DeviceSn
                             + "&time=" + timestamp
                             + "&ver=" + ProtocolCredentials.Ver
                             + "&vn=" + ProtocolCredentials.Vn;

            var sign = Md5Hex(parameters + ProtocolCredentials.AppKey);
            var url = host + "/i.php?" + parameters + "&sign=" + sign;

            var response = await _httpClient.GetAsync(url, RequestTimeoutMs, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess) return null;

            var code = MiniJson.ReadInt(response.Body, "code");
            if (code != 200) return null;

            var token = MiniJson.ReadString(response.Body, "token");
            if (string.IsNullOrEmpty(token)) return null;

            lock (_orderLock)
            {
                _tokens[host] = token;
            }
            return token;
        }

        private async Task<string> FetchSongUrlAsync(
            string host,
            string musicNo,
            string token,
            string resolution,
            bool preferH265,
            CancellationToken cancellationToken)
        {
            var url = await RequestSongUrlOnceAsync(
                host, musicNo, token, resolution, preferH265, cancellationToken).ConfigureAwait(false);

            if (url != null) return url;

            // 被风控限流时等 3 秒用同一 token 再试一次。
            await Task.Delay(BlockedRetryDelayMs, cancellationToken).ConfigureAwait(false);

            return await RequestSongUrlOnceAsync(
                host, musicNo, token, resolution, preferH265, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> RequestSongUrlOnceAsync(
            string host,
            string musicNo,
            string token,
            string resolution,
            bool preferH265,
            CancellationToken cancellationToken)
        {
            var timestamp = GetUnixTimestamp();

            // 参数顺序、原始 token 中的 "=" 以及签名拼接必须与官方一致。
            var parameters = "appid=" + ProtocolCredentials.AppId
                             + "&device=" + ProtocolCredentials.DeviceMac + "_" + ProtocolCredentials.DeviceSn
                             + "&ish265=" + (preferH265 ? "1" : "0")
                             + "&ls=1"
                             + "&musicno=" + musicNo
                             + "&resolution=" + (string.IsNullOrWhiteSpace(resolution) ? "720" : resolution)
                             + "&sn=" + ProtocolCredentials.DeviceSn
                             + "&time=" + timestamp
                             + "&token=" + token;

            var sign = Md5Hex(parameters + ProtocolCredentials.SdkKey);
            var url = host + "/music/do.php?" + parameters + "&sign=" + sign;

            var response = await _httpClient.GetAsync(url, RequestTimeoutMs, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsBlocked)
            {
                _logger.Warn("源站限流 403: " + host);
                return null;
            }

            if (!response.IsSuccess) return null;

            var code = MiniJson.ReadInt(response.Body, "code");
            if (code != 200) return null;

            var data = MiniJson.ReadString(response.Body, "data");
            if (string.IsNullOrEmpty(data)) return null;

            if (!data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !data.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;

            return data;
        }

        #endregion

        #region 源优先级调整

        private void Demote(string host)
        {
            lock (_orderLock)
            {
                var index = _hostOrder.IndexOf(host);
                if (index < 0 || index >= _hostOrder.Count - 1) return;

                _hostOrder.RemoveAt(index);
                _hostOrder.Add(host);
            }
        }

        private void Promote(string host)
        {
            lock (_orderLock)
            {
                var index = _hostOrder.IndexOf(host);
                if (index <= 0) return;

                _hostOrder.RemoveAt(index);
                _hostOrder.Insert(0, host);
            }
        }

        #endregion

        #region 工具

        private static string GetUnixTimestamp()
        {
            var seconds = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return seconds.ToString(CultureInfo.InvariantCulture);
        }

        private static string Md5Hex(string value)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        #endregion
    }
}
