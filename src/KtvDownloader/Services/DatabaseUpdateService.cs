using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Http;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Infrastructure.Security;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 曲库更新服务。复刻官方 Muse 曲库同步协议。
    /// <para>
    /// 端点、设备号、RSA 公钥都在 <see cref="ProtocolCredentials"/>，
    /// 可用 <c>%AppData%\KtvDownloader\credentials.json</c> 覆盖。
    /// </para>
    /// <para>
    /// 磁盘占用要心里有数：下载的 .gz 约 418MB，解压出的 muse.db 约 983MB，
    /// 安装时旧库还会被改名保留为 <c>muse.db.backup</c>（约 983MB），
    /// 加上正式文件，峰值需要约 3GB 空闲空间。<see cref="UpdateAsync"/> 会先检查。
    /// </para>
    /// </summary>
    public sealed class DatabaseUpdateService : IDatabaseUpdateService, IDisposable
    {
        /// <summary>同步接口里代表"曲库文件"的类型号。</summary>
        private const int MusicFileType = 214;

        /// <summary>更新所需的最小空闲空间（约 3GB，见类注释）。</summary>
        private const long MinimumFreeBytes = 3L * 1024 * 1024 * 1024;

        private const string VersionFileName = "db_version.txt";

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        /// <summary>0 = 未释放，1 = 已释放。</summary>
        private int _disposed;

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

            EnsureFreeDiskSpace(targetDirectory);

            var gzPath = targetFullPath + ".gz.download";
            var dbPath = targetFullPath + ".db.download";

            // 上次失败留下的半成品会白白占掉 1GB，先清掉。
            SafeDelete(gzPath);
            SafeDelete(dbPath);

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
                Report(progress, "正在校验曲库可用性");

                // MD5 对得上只能说明"下载完整"，不能说明"这是一个能用的 SQLite 曲库"。
                // 装上去再发现打不开，用户的旧曲库已经被改名备份了，体验极差 ——
                // 所以在替换正式文件之前先真打开一次。
                VerifySqliteDatabase(dbPath);

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
                    ProtocolCredentials.RsaPublicKey,
                    "{\"device_id\":\"" + ProtocolCredentials.DatabaseDeviceId + "\"}");

                var body = "{"
                    + "\"encrypted_data\":" + MiniJson.Quote(encrypted) + ","
                    + "\"ip\":\"" + ProtocolCredentials.SpoofedIp + "\","
                    + "\"dns\":\"\","
                    + "\"router\":\"\","
                    + "\"subnet_mask\":\"255.255.255.0\","
                    + "\"channel\":\"" + ProtocolCredentials.Channel + "\","
                    + "\"mode\":\"" + ProtocolCredentials.Mode + "\""
                    + "}";

                using (var request = new HttpRequestMessage(HttpMethod.Post, ProtocolCredentials.LoginUrl))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("User-Agent", ProtocolCredentials.UserAgent);

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

                using (var request = new HttpRequestMessage(HttpMethod.Post, ProtocolCredentials.SyncUrl))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("User-Agent", ProtocolCredentials.UserAgent);
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
                Url = MiniJson.ReadString(fileJson, "url"),
                Md5 = MiniJson.ReadString(fileJson, "md5")
            };

            // 版本优先取 date（毫秒时间戳），其次 day，再次 db_vc。
            var date = MiniJson.ReadRaw(fileJson, "date");
            if (!string.IsNullOrEmpty(date) && date != "null")
            {
                info.Version = date;
            }
            else
            {
                var day = MiniJson.ReadString(fileJson, "day");
                if (!string.IsNullOrEmpty(day)) info.Version = day;
                else info.Version = MiniJson.ReadRaw(fileJson, "db_vc");
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
                request.Headers.TryAddWithoutValidation("User-Agent", ProtocolCredentials.UserAgent);

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

        /// <summary>
        /// 检查目标磁盘的剩余空间，不够就直接失败，别下到一半才报"磁盘已满"。
        /// </summary>
        private static void EnsureFreeDiskSpace(string directory)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(directory));
                if (string.IsNullOrEmpty(root)) return;

                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < MinimumFreeBytes)
                {
                    throw new InvalidOperationException(string.Format(
                        "磁盘空间不足：{0} 只剩 {1:F1}GB，曲库更新峰值需要约 {2:F1}GB" +
                        "（下载包 + 解压出的曲库 + 旧曲库备份）。",
                        root, free / (1024d * 1024 * 1024), MinimumFreeBytes / (1024d * 1024 * 1024)));
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception)
            {
                // 拿不到磁盘信息（网络路径等）就不拦，让后续步骤自己报错。
            }
        }

        /// <summary>
        /// 真打开一次解压出来的曲库：跑 <c>PRAGMA quick_check</c> 并数一下 songs 行数。
        /// 结果不是 ok、或者一首歌都没有，都视为"这不是可用曲库"而拒绝安装。
        /// </summary>
        private void VerifySqliteDatabase(string databasePath)
        {
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = databasePath,
                ReadOnly = true,
                FailIfMissing = true,
                Pooling = false
            };

            using (var connection = new SQLiteConnection(builder.ToString()))
            {
                connection.Open();

                string checkResult;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA quick_check";
                    command.CommandTimeout = 600;
                    checkResult = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                if (!string.Equals(checkResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error("解压出的曲库未通过 quick_check: " + checkResult);
                    throw new InvalidOperationException("下载的文件不是完好的 SQLite 曲库（quick_check=" +
                                                        checkResult + "）");
                }

                long songCount;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM songs";
                    command.CommandTimeout = 300;
                    songCount = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                if (songCount <= 0)
                {
                    throw new InvalidOperationException("曲库校验通过但 songs 表为空，拒绝安装");
                }

                _logger.Info("新曲库校验通过，songs 行数=" + songCount);
            }
        }

        private void InstallDatabase(string targetPath, string newDatabasePath)
        {
            var backupPath = targetPath + ".backup";

            SafeDelete(backupPath);

            if (File.Exists(targetPath))
            {
                File.Move(targetPath, backupPath);
                _logger.Info("旧曲库已备份到 " + backupPath +
                             "（约 1GB，确认新曲库可用后可以手动删除以回收空间）");
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

        /// <summary>
        /// 释放内部 HttpClient。
        /// <para>
        /// 主界面每次保存设置 / 更新曲库都会重建服务，不释放就会累积连接池与 socket。
        /// </para>
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try { _httpClient.Dispose(); } catch { }
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
