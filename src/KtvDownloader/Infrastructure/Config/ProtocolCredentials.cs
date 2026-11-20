using System;
using System.Collections.Generic;
using System.IO;
using KtvDownloader.Models;

namespace KtvDownloader.Infrastructure.Config
{
    /// <summary>
    /// 外部协议凭据与端点的**唯一**存放处。
    /// <para>
    /// 为什么要有这个类：这些值原本以 <c>private const</c> 散落在
    /// <c>SongUrlService</c> 与 <c>DatabaseUpdateService</c> 里。
    /// 源站换密钥 / 换域名 / 试用窗口到期是常事，散着放就意味着每次都要改代码重新编译，
    /// 而且没人能一眼看全"这个程序对外用了哪些身份"。
    /// </para>
    /// <para>
    /// 现在：默认值写在代码里（保证开箱可用），
    /// 同时支持从 <c>%AppData%\KtvDownloader\credentials.json</c> 覆盖，
    /// 密钥轮换时用户改一个 JSON 文件即可，无需重新构建。
    /// 文件缺失或格式非法时静默回落到默认值。
    /// </para>
    /// </summary>
    public static class ProtocolCredentials
    {
        // ── 歌曲取址（i.php / do.php）──────────────────────────────

        /// <summary>取址 appid。</summary>
        public const string DefaultAppId = "d4eeacc6cec3434fbc8c41608a3056a0";

        /// <summary>i.php 的签名盐。</summary>
        public const string DefaultAppKey = "024210cba40d4385a93e6c2d3249bfb5";

        /// <summary>do.php 的签名盐。</summary>
        public const string DefaultSdkKey = "19042303a8374f67ae3fe1e25c97936f";

        /// <summary>协议版本。</summary>
        public const string DefaultVer = "4";

        /// <summary>客户端版本号。</summary>
        public const string DefaultVn = "4.100.5.11121504";

        /// <summary>
        /// 设备标识。官方 App 会用真实网卡 MAC，这里用固定的合法测试值。
        /// <para>试用窗口（i.php 返回的 end_date）到期后，换掉这两个值 + 出口 IP 即可恢复。</para>
        /// </summary>
        public const string DefaultDeviceMac = "9658dd1eafa73a55";

        /// <summary>设备序列号。</summary>
        public const string DefaultDeviceSn = "0000037bb640";

        /// <summary>
        /// 源站优先级（逗号分隔可覆盖）。运行时失败的源会沉到队尾，成功的提到队首。
        /// </summary>
        public const string DefaultHosts =
            "http://zk.ktv1.top:6677," +
            "http://w.w345.my," +
            "http://desktok.top," +
            "http://mm.kk456.top," +
            "http://w.w345.my:66," +
            "http://z.w345.my," +
            "http://b.sktv.top," +
            "http://w345.my";

        // ── 曲库同步（mls-api）────────────────────────────────────

        /// <summary>曲库登录地址。</summary>
        public const string DefaultLoginUrl = "https://mconn.cherryonline.cn/mls-api/v1/login";

        /// <summary>曲库同步地址。</summary>
        public const string DefaultSyncUrl = "https://mconn.cherryonline.cn/mls-api/v1/sqlite/sync";

        /// <summary>曲库服务认可的设备标识。</summary>
        public const string DefaultDatabaseDeviceId = "s00e04b021b2d";

        /// <summary>登录请求里上报的 IP（官方 App 报的是内网地址）。</summary>
        public const string DefaultSpoofedIp = "172.16.32.15";

        /// <summary>渠道。</summary>
        public const string DefaultChannel = "common";

        /// <summary>模式。</summary>
        public const string DefaultMode = "ott";

        /// <summary>曲库接口的 User-Agent（这套接口需要完整 UA，与歌曲源的风控通道不同）。</summary>
        public const string DefaultUserAgent = "MaidongKTV/1.2";

        /// <summary>曲库登录用的 RSA 公钥（X.509 SPKI, Base64）。</summary>
        public const string DefaultRsaPublicKey =
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAucL0oFErd7REM6TSNa3EZdN1YaOs4J1eCybLPyoQ" +
            "9ru3q1HU67agC9FzhrCG/RvAQUya5iPmQ8Caed05vqcCVcyJChmkSOGQ7DVShe2rGuTMNlpoRV6UzfcraaVS++7m2K/" +
            "+kSZJ8OAhhhVuqPruMjsFYpdtstAwvyZT28b+eENwzpp9UHqsooZc7FZ0H8kTbs6XMkw4nIWo+4HoPAhNLEY+xdHvwY" +
            "6drF/3WDTvsaoMrs73TVQCEEHzZNIz2H/is9VLMnIyOfnfcJi9br78Fj2xHzxu3sAySBOTVLmUMxqYh/g1ox5OXGcW9" +
            "3HJkQLkBi42tFAEkWYlYyl93+jbbQIDAQAB";

        private static readonly object Sync = new object();
        private static Dictionary<string, string> _overrides;

        /// <summary>覆盖文件路径。</summary>
        public static string OverrideFilePath =>
            Path.Combine(AppConfig.AppDataDirectory, "credentials.json");

        /// <summary>取址 appid。</summary>
        public static string AppId => Get("AppId", DefaultAppId);

        /// <summary>i.php 签名盐。</summary>
        public static string AppKey => Get("AppKey", DefaultAppKey);

        /// <summary>do.php 签名盐。</summary>
        public static string SdkKey => Get("SdkKey", DefaultSdkKey);

        /// <summary>协议版本。</summary>
        public static string Ver => Get("Ver", DefaultVer);

        /// <summary>客户端版本号。</summary>
        public static string Vn => Get("Vn", DefaultVn);

        /// <summary>设备 MAC。</summary>
        public static string DeviceMac => Get("DeviceMac", DefaultDeviceMac);

        /// <summary>设备序列号。</summary>
        public static string DeviceSn => Get("DeviceSn", DefaultDeviceSn);

        /// <summary>曲库登录地址。</summary>
        public static string LoginUrl => Get("LoginUrl", DefaultLoginUrl);

        /// <summary>曲库同步地址。</summary>
        public static string SyncUrl => Get("SyncUrl", DefaultSyncUrl);

        /// <summary>曲库设备标识。</summary>
        public static string DatabaseDeviceId => Get("DatabaseDeviceId", DefaultDatabaseDeviceId);

        /// <summary>登录上报的 IP。</summary>
        public static string SpoofedIp => Get("SpoofedIp", DefaultSpoofedIp);

        /// <summary>渠道。</summary>
        public static string Channel => Get("Channel", DefaultChannel);

        /// <summary>模式。</summary>
        public static string Mode => Get("Mode", DefaultMode);

        /// <summary>曲库接口 UA。</summary>
        public static string UserAgent => Get("UserAgent", DefaultUserAgent);

        /// <summary>RSA 公钥。</summary>
        public static string RsaPublicKey => Get("RsaPublicKey", DefaultRsaPublicKey);

        /// <summary>源站列表，按优先级从高到低。</summary>
        public static IReadOnlyList<string> Hosts
        {
            get
            {
                var result = new List<string>();
                foreach (var host in Get("Hosts", DefaultHosts).Split(','))
                {
                    var value = host.Trim();
                    if (value.Length > 0) result.Add(value);
                }
                return result;
            }
        }

        /// <summary>
        /// 生成一份覆盖文件模板，方便用户照着改。
        /// </summary>
        public static string BuildTemplate()
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"_说明\": " +
                               MiniJson.Quote("只保留需要覆盖的键，删掉的键使用内置默认值。" +
                                              "改完重启程序生效。") + ",");
            builder.AppendLine("  \"AppId\": " + MiniJson.Quote(DefaultAppId) + ",");
            builder.AppendLine("  \"AppKey\": " + MiniJson.Quote(DefaultAppKey) + ",");
            builder.AppendLine("  \"SdkKey\": " + MiniJson.Quote(DefaultSdkKey) + ",");
            builder.AppendLine("  \"DeviceMac\": " + MiniJson.Quote(DefaultDeviceMac) + ",");
            builder.AppendLine("  \"DeviceSn\": " + MiniJson.Quote(DefaultDeviceSn) + ",");
            builder.AppendLine("  \"Hosts\": " + MiniJson.Quote(DefaultHosts) + ",");
            builder.AppendLine("  \"DatabaseDeviceId\": " + MiniJson.Quote(DefaultDatabaseDeviceId) + ",");
            builder.AppendLine("  \"SpoofedIp\": " + MiniJson.Quote(DefaultSpoofedIp) + ",");
            builder.AppendLine("  \"UserAgent\": " + MiniJson.Quote(DefaultUserAgent));
            builder.AppendLine("}");
            return builder.ToString();
        }

        /// <summary>强制重新读取覆盖文件。</summary>
        public static void Reload()
        {
            lock (Sync)
            {
                _overrides = null;
            }
        }

        private static string Get(string key, string fallback)
        {
            var overrides = LoadOverrides();
            if (overrides == null) return fallback;

            string value;
            if (overrides.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();

            return fallback;
        }

        private static Dictionary<string, string> LoadOverrides()
        {
            lock (Sync)
            {
                if (_overrides != null) return _overrides;

                try
                {
                    var path = OverrideFilePath;
                    if (!File.Exists(path))
                    {
                        _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        return _overrides;
                    }

                    var parsed = MiniJson.ParseObject(File.ReadAllText(path, System.Text.Encoding.UTF8));
                    _overrides = parsed ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    // 覆盖文件坏了不能影响程序运行，回落到内置默认值。
                    _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                return _overrides;
            }
        }
    }
}
