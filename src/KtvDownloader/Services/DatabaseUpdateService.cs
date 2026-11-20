using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Http;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Infrastructure.Security;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 曲库更新服务。复刻官方 Muse 曲库同步协议。
    /// </summary>
    public sealed class DatabaseUpdateService : IDatabaseUpdateService
    {
        private const string LoginUrl = "https://mconn.cherryonline.cn/mls-api/v1/login";
        private const string SyncUrl = "https://mconn.cherryonline.cn/mls-api/v1/sqlite/sync";
        private const int MusicFileType = 214;
        private const string Channel = "common";
        private const string Mode = "ott";
        private const string SpoofedIp = "172.16.32.15";

        /// <summary>官方曲库服务认可的设备标识。</summary>
        private const string DeviceId = "s00e04b021b2d";

        /// <summary>曲库登录用的 RSA 公钥（X.509 SPKI, Base64）。</summary>
        private const string RsaPublicKey =
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAucL0oFErd7REM6TSNa3EZdN1YaOs4J1eCybLPyoQ" +
            "9ru3q1HU67agC9FzhrCG/RvAQUya5iPmQ8Caed05vqcCVcyJChmkSOGQ7DVShe2rGuTMNlpoRV6UzfcraaVS++7m2K/" +
            "+kSZJ8OAhhhVuqPruMjsFYpdtstAwvyZT28b+eENwzpp9UHqsooZc7FZ0H8kTbs6XMkw4nIWo+4HoPAhNLEY+xdHvwY" +
            "6drF/3WDTvsaoMrs73TVQCEEHzZNIz2H/is9VLMnIyOfnfcJi9br78Fj2xHzxu3sAySBOTVLmUMxqYh/g1ox5OXGcW9" +
            "3HJkQLkBi42tFAEkWYlYyl93+jbbQIDAQAB";

        private const string VersionFileName = "db_version.txt";
        private const string UserAgent = "MaidongKTV/1.2";

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public DatabaseUpdateService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 曲库接口走标准 HTTPS，需要完整 TLS 与 UA，与歌曲源的风控通道不同。
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public string ReadLocalVersion(string localDatabasePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(localDatabasePath)) return null;

                var directory = Path.GetDirectoryName(localDatabasePath);
                if (string.IsNullOrEmpty(directory)) return null;

                var versionFile = Path.Combine(directory, VersionFileName);
                if (!File.Exists(versionFile)) return null;

                var text = File.ReadAllText(versionFile, Encoding.UTF8).Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }

        public async Task<DatabaseVersionInfo> CheckUpdateAsync(
            string localDatabasePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var localVersion = ReadLocalVersion(localDatabasePath);
            _logger.Info("检查曲库更新，本地版本=" + (localVersion ?? "未知"));

            var cookie = await LoginAsync(cancellationToken).ConfigureAwait(false);
            if (cookie == null)
                throw new InvalidOperationException("曲库服务登录失败，请检查网络连接");

            var file = await SyncAsync(cookie, cancellationToken).ConfigureAwait(false);
            if (file == null)
                throw new InvalidOperationException("曲库同步接口未返回可用文件");

            _logger.Info("远端曲库版本=" + file.Version + " md5=" + file.Md5);

            return new DatabaseVersionInfo
            {
                Version = file.Version,
                Url = file.Url,
                Md5 = file.Md5,
                LocalVersion = localVersion
            };
        }

        public async Task<string> UpdateAsync(
            string targetDatabasePath,
            IProgress<DatabaseUpdateProgress> progress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(targetDatabasePath))
                throw new ArgumentException("曲库目标路径为空", nameof(targetDatabasePath));

            Report(progress, "正在登录曲库服务");

            var cookie = await LoginAsync(cancellationToken).ConfigureAwait(false);
            if (cookie == null)
                throw new InvalidOperationException("曲库服务登录失败，请检查网络连接");

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, "正在查询曲库版本");

            var file = await SyncAsync(cookie, cancellationToken).ConfigureAwait(false);
            if (file == null)
                throw new InvalidOperationException("曲库同步接口未返回可用文件");

            _logger.Info("开始下载曲库 版本=" + file.Version);

            // 全部准备工作在临时目录完成，成功后才替换正式文件，避免中途失败毁掉现有曲库。
            var targetFullPath = Path.GetFullPath(targetDatabasePath);
            var targetDirectory = Path.GetDirectoryName(targetFullPath);
            if (string.IsNullOrEmpty(targetDirectory))
                throw new InvalidOperationException("曲库目标目录非法");

            if (!Directory.Exists(targetDirectory)) Directory.CreateDirectory(targetDirectory);

            var gzPath = targetFullPath + ".gz.download";
            var dbPath = targetFullPath + ".db.download";

            try
            {
                await DownloadFileAsync(file.Url, gzPath, progress, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(file.Md5))
                {
                    Report(progress, "正在校验文件完整性");
                    var actual = ComputeMd5(gzPath);
                    if (!string.Equals(actual, file.Md5, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("曲库文件校验失败，MD5 不匹配");
                }

                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, "正在解压曲库");

                await Task.Run(() => DecompressGzip(gzPath, dbPath, progress, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, "正在安装曲库");

                InstallDatabase(targetFullPath, dbPath);
                SaveVersion(targetFullPath, file.Version);

                _logger.Info("曲库更新完成，版本=" + file.Version);
                Report(progress, "曲库更新完成");

                return targetFullPath;
            }
            finally
            {
                SafeDelete(gzPath);
                SafeDelete(dbPath);
            }
        }

        #region 协议实现

        private async Task<string> LoginAsync(CancellationToken cancellationToken)
        {
            try
            {
                var encrypted = RsaPkcs1Encryptor.EncryptToBase64(
                    RsaPublicKey,
                    "{\"device_id\":\"" + DeviceId + "\"}");

                var body = "{"
                    + "\"encrypted_data\":" + QuoteJson(encrypted) + ","
                    + "\"ip\":\"" + SpoofedIp + "\","
                    + "\"dns\":\"\","
                    + "\"router\":\"\","
                    + "\"subnet_mask\":\"255.255.255.0\","
                    + "\"channel\":\"" + Channel + "\","
                    + "\"mode\":\"" + Mode + "\""
                    + "}";

                using (var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Warn("曲库登录 HTTP " + (int)response.StatusCode);
                            return null;
                        }

                        // 需要从 Set-Cookie 取 muse-portal-connector
                        System.Collections.Generic.IEnumerable<string> cookies;
                        if (response.Headers.TryGetValues("Set-Cookie", out cookies))
                        {
                            foreach (var cookie in cookies)
                            {
                                var first = cookie.Split(';')[0].Trim();
                                if (first.StartsWith("muse-portal-connector=", StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.Info("曲库登录成功");
                                    return first;
                                }
                            }
                        }

                        _logger.Warn("曲库登录未返回 muse-portal-connector");
                        return null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("曲库登录异常", ex);
                return null;
            }
        }

        private async Task<RemoteFileInfo> SyncAsync(string cookie, CancellationToken cancellationToken)
        {
            try
            {
                var body = "{\"last_sync_at\":0,\"music_file_type\":" +
                           MusicFileType.ToString(CultureInfo.InvariantCulture) + "}";

                using (var request = new HttpRequestMessage(HttpMethod.Post, SyncUrl))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    request.Headers.TryAddWithoutValidation("Cookie", cookie);

                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Warn("曲库同步 HTTP " + (int)response.StatusCode);
                            return null;
                        }

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return ParseSyncResponse(json);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("曲库同步异常", ex);
                return null;
            }
        }

        /// <summary>
        /// 从同步响应里取出 files[0] 的 url / md5 / version。
        /// 响应结构：{"files":[{"url":"...","md5":"...","date":1795047075411,...}]}
        /// </summary>
        private RemoteFileInfo ParseSyncResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var filesIndex = json.IndexOf("\"files\"", StringComparison.Ordinal);
            if (filesIndex < 0) return null;

            var arrayStart = json.IndexOf('[', filesIndex);
            if (arrayStart < 0) return null;

            var objectStart = json.IndexOf('{', arrayStart);
            if (objectStart < 0) return null;

            var objectEnd = FindMatchingBrace(json, objectStart);
            if (objectEnd < 0) return null;

            var fileJson = json.Substring(objectStart, objectEnd - objectStart + 1);

            var info = new RemoteFileInfo
            {
                Url = ReadJsonString(fileJson, "url"),
                Md5 = ReadJsonString(fileJson, "md5")
            };

            // 版本优先取 date（毫秒时间戳），其次 day，再次 db_vc。
            var date = ReadJsonRaw(fileJson, "date");
            if (!string.IsNullOrEmpty(date) && date != "null")
            {
                info.Version = date;
            }
            else
            {
                var day = ReadJsonString(fileJson, "day");
                if (!string.IsNullOrEmpty(day)) info.Version = day;
                else info.Version = ReadJsonRaw(fileJson, "db_vc");
            }

            if (string.IsNullOrWhiteSpace(info.Url)) return null;
            return info;
        }

        private static int FindMatchingBrace(string json, int start)
        {
            var depth = 0;
            var inString = false;
            for (var i = start; i < json.Length; i++)
            {
                var c = json[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static string ReadJsonString(string json, string key)
        {
            var value = ReadJsonRaw(json, key);
            if (value == null) return null;
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2);
            return null;
        }

        private static string ReadJsonRaw(string json, string key)
        {
            var pattern = "\"" + key + "\"";
            var index = json.IndexOf(pattern, StringComparison.Ordinal);
            if (index < 0) return null;

            var colon = json.IndexOf(':', index + pattern.Length);
            if (colon < 0) return null;

            var i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return null;

            if (json[i] == '"')
            {
                var start = i;
                i++;
                while (i < json.Length)
                {
                    if (json[i] == '\\') { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                return json.Substring(start, i - start);
            }

            var valueStart = i;
            while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
            return json.Substring(valueStart, i - valueStart).Trim();
        }

        private static string QuoteJson(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var c in value)
            {
                if (c == '"' || c == '\\') builder.Append('\\');
                builder.Append(c);
            }
            builder.Append('"');
            return builder.ToString();
        }

        #endregion

        #region 下载与文件处理

        private async Task DownloadFileAsync(
            string url,
            string destinationPath,
            IProgress<DatabaseUpdateProgress> progress,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("Accept", "*/*");
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using (var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    var total = response.Content.Headers.ContentLength;

                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var destination = new FileStream(destinationPath, FileMode.Create,
                        FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
                    {
                        var buffer = new byte[256 * 1024];
                        long received = 0;
                        var lastReported = -1;

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                                .ConfigureAwait(false);
                            if (read <= 0) break;

                            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            received += read;

                            // 每变化 1% 才上报，避免 UI 线程被高频进度刷爆。
                            if (total.HasValue && total.Value > 0)
                            {
                                var percent = (int)(received * 100 / total.Value);
                                if (percent == lastReported) continue;
                                lastReported = percent;
                            }

                            Report(progress, "正在下载曲库", received, total);
                        }
                    }
                }
            }
        }

        private static void DecompressGzip(
            string sourcePath,
            string destinationPath,
            IProgress<DatabaseUpdateProgress> progress,
            CancellationToken cancellationToken)
        {
            using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 256 * 1024))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 256 * 1024))
            {
                var buffer = new byte[256 * 1024];
                long written = 0;
                var lastMegabyte = -1L;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var read = gzip.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    output.Write(buffer, 0, read);
                    written += read;

                    // 解压约 1GB，每 64MB 报一次，避免界面像卡死。
                    var megabyte = written / (64L * 1024 * 1024);
                    if (megabyte == lastMegabyte) continue;
                    lastMegabyte = megabyte;

                    progress?.Report(new DatabaseUpdateProgress(
                        "正在解压曲库 " + (written / (1024 * 1024)) + "MB"));
                }
            }
        }

        private void InstallDatabase(string targetPath, string newDatabasePath)
        {
            var backupPath = targetPath + ".backup";

            SafeDelete(backupPath);

            if (File.Exists(targetPath))
            {
                File.Move(targetPath, backupPath);
                _logger.Info("旧曲库已备份到 " + backupPath);
            }

            try
            {
                File.Move(newDatabasePath, targetPath);
            }
            catch
            {
                // 安装失败必须把旧曲库恢复回去。
                if (File.Exists(backupPath) && !File.Exists(targetPath))
                    File.Move(backupPath, targetPath);
                throw;
            }

            // 顺手清掉 sqlite 的 wal/shm 残留，否则下次打开可能读到旧页。
            SafeDelete(targetPath + "-wal");
            SafeDelete(targetPath + "-shm");
        }

        private void SaveVersion(string databasePath, string version)
        {
            try
            {
                var directory = Path.GetDirectoryName(databasePath);
                if (string.IsNullOrEmpty(directory)) return;

                var versionFile = Path.Combine(directory, VersionFileName);
                File.WriteAllText(versionFile, version ?? string.Empty, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _logger.Warn("写入曲库版本文件失败: " + ex.Message);
            }
        }

        private static string ComputeMd5(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 256 * 1024))
            {
                var hash = md5.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 清理失败不影响主流程。
            }
        }

        private static void Report(
            IProgress<DatabaseUpdateProgress> progress,
            string stage,
            long received = 0,
            long? total = null)
        {
            progress?.Report(new DatabaseUpdateProgress(stage, received, total));
        }

        #endregion

        /// <summary>同步接口返回的文件信息。</summary>
        private sealed class RemoteFileInfo
        {
            public string Url { get; set; }
            public string Md5 { get; set; }
            public string Version { get; set; }
        }
    }
}
