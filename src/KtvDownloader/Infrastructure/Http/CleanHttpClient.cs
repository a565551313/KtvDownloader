using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Logging;

namespace KtvDownloader.Infrastructure.Http
{
    /// <summary>
    /// <see cref="ICleanHttpClient"/> 的裸 Socket 实现。
    /// <para>
    /// 关键点：请求报文只写 <c>GET path HTTP/1.1</c> + <c>Host</c> + <c>Accept: */*</c>，
    /// 不写任何其它头。这正是官方 App 抓包看到的形态，也是唯一能通过源站风控的形态
    /// （多带一个 <c>User-Agent</c> 就 403，见 docs §坑2）。
    /// 因此这里也**不能**加 <c>Connection: close</c> —— 那属于"多带一个头"，
    /// 需要真机验证过才能动；取而代之的做法是给读操作设截止时间（见 <see cref="ReadToEnd"/>）。
    /// </para>
    /// </summary>
    public sealed class CleanHttpClient : ICleanHttpClient
    {
        /// <summary>连接阶段的最小/最大超时。</summary>
        private const int MinTimeoutMs = 1000;
        private const int MaxTimeoutMs = 30000;

        private readonly ILogger _logger;
        private readonly bool _acceptAnyTlsCertificate;

        public CleanHttpClient(ILogger logger)
            : this(logger, false)
        {
        }

        /// <param name="acceptAnyTlsCertificate">
        /// true 表示跳过 TLS 证书校验。
        /// <para>
        /// 默认 **false**：早期版本无条件 <c>(sender, cert, chain, errors) => true</c>，
        /// 等于任何中间人都能冒充源站。当前源站都是 http，所以这条路径平时走不到，
        /// 但一旦哪天换成 https，那就是完整的中间人缺口。只有在明确知道对端证书有问题、
        /// 且链路本身可信时才把它打开。
        /// </para>
        /// </param>
        public CleanHttpClient(ILogger logger, bool acceptAnyTlsCertificate)
        {
            _logger = logger;
            _acceptAnyTlsCertificate = acceptAnyTlsCertificate;
        }

        public Task<HttpResult> GetAsync(
            string url,
            int timeoutMs = 10000,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(url))
                return Task.FromResult(HttpResult.Fail("URL 为空"));

            return Task.Run(() => GetCore(url, timeoutMs, cancellationToken), cancellationToken);
        }

        private HttpResult GetCore(string url, int timeoutMs, CancellationToken cancellationToken)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return HttpResult.Fail("URL 格式非法: " + url);

            var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            if (!isHttps && !uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                return HttpResult.Fail("不支持的协议: " + uri.Scheme);

            var timeout = Math.Max(MinTimeoutMs, Math.Min(timeoutMs, MaxTimeoutMs));
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);

            Socket socket = null;
            Stream stream = null;
            var registration = default(CancellationTokenRegistration);

            try
            {
                var port = uri.Port > 0 ? uri.Port : (isHttps ? 443 : 80);

                socket = ConnectWithTimeout(uri.Host, port, timeout, cancellationToken);
                socket.ReceiveTimeout = timeout;
                socket.SendTimeout = timeout;

                stream = new NetworkStream(socket, false);
                if (isHttps)
                {
                    var ssl = new SslStream(stream, false, OnCertificateValidation);
                    ssl.AuthenticateAsClient(uri.Host);
                    stream = ssl;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 阻塞式 socket 读不认 CancellationToken。取消时直接关掉 socket，
                // 让正在进行的读立刻抛异常，然后下面按取消处理。
                var socketToClose = socket;
                registration = cancellationToken.Register(() =>
                {
                    try { socketToClose.Close(); } catch { }
                });

                var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
                var request = new StringBuilder();
                request.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
                request.Append("Host: ").Append(uri.Host).Append("\r\n");
                request.Append("Accept: */*\r\n");
                request.Append("\r\n");

                var requestBytes = Encoding.ASCII.GetBytes(request.ToString());
                stream.Write(requestBytes, 0, requestBytes.Length);
                stream.Flush();

                cancellationToken.ThrowIfCancellationRequested();

                return ReadResponse(stream, deadline, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // socket 被取消回调关掉时会抛 SocketException/ObjectDisposedException，
                // 这时候真正的语义是"已取消"，要还原成 OperationCanceledException。
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                _logger.Warn("HTTP 请求失败 " + url + " : " + ex.GetType().Name + " " + ex.Message);
                return HttpResult.Fail(ex.Message);
            }
            finally
            {
                registration.Dispose();

                if (stream != null)
                {
                    try { stream.Dispose(); } catch { }
                }

                if (socket != null)
                {
                    try { socket.Close(); } catch { }
                    socket.Dispose();
                }
            }
        }

        /// <summary>
        /// 带超时的连接。
        /// <para>
        /// 两个必须修的点：
        /// 1. 早期版本写死 <c>AddressFamily.InterNetwork</c>，IPv6-only / 双栈网络下
        ///    域名只有 AAAA 记录时直接连不上；现在按 DNS 返回的地址族逐个尝试（IPv4 优先）。
        /// 2. <c>Socket.ReceiveTimeout</c> 对 <c>Connect</c> 无效，阻塞式 Connect 会挂到
        ///    操作系统默认超时（约 21 秒），调用方给的 timeoutMs 形同虚设；
        ///    现在用 BeginConnect + WaitOne 精确控制。
        /// </para>
        /// </summary>
        private Socket ConnectWithTimeout(
            string host,
            int port,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("DNS 解析失败: " + host + " (" + ex.Message + ")", ex);
            }

            if (addresses == null || addresses.Length == 0)
                throw new InvalidOperationException("DNS 未返回任何地址: " + host);

            // IPv4 优先：源站基本只有 A 记录，而不少网络的 IPv6 出口会先超时再回退。
            var ordered = addresses
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToArray();

            var lastError = "无可用地址";

            foreach (var address in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                var connected = false;
                try
                {
                    socket.NoDelay = true;
                    connected = TryConnect(socket, address, port, timeoutMs);
                    if (connected) return socket;

                    lastError = "连接超时 " + address + ":" + port;
                }
                catch (Exception ex)
                {
                    lastError = address + ":" + port + " " + ex.GetType().Name + " " + ex.Message;
                }
                finally
                {
                    if (!connected)
                    {
                        try { socket.Dispose(); } catch { }
                    }
                }
            }

            throw new InvalidOperationException(
                "无法连接到 " + host + ":" + port + "（" + lastError + "）");
        }

        private static bool TryConnect(Socket socket, IPAddress address, int port, int timeoutMs)
        {
            var asyncResult = socket.BeginConnect(address, port, null, null);
            if (!asyncResult.AsyncWaitHandle.WaitOne(timeoutMs)) return false;

            socket.EndConnect(asyncResult);
            return true;
        }

        private bool OnCertificateValidation(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors errors)
        {
            if (_acceptAnyTlsCertificate) return true;

            if (errors == SslPolicyErrors.None) return true;

            _logger.Warn("TLS 证书校验失败: " + errors);
            return false;
        }

        private HttpResult ReadResponse(
            Stream stream,
            DateTime deadline,
            CancellationToken cancellationToken)
        {
            // 读状态行
            var statusLine = ReadAsciiLine(stream);
            if (statusLine == null) return HttpResult.Fail("响应为空");

            var statusParts = statusLine.Split(' ');
            var statusCode = 0;
            if (statusParts.Length >= 2)
                int.TryParse(statusParts[1], out statusCode);

            // 读响应头
            long contentLength = -1;
            var chunked = false;
            while (true)
            {
                var line = ReadAsciiLine(stream);
                if (line == null) break;
                if (line.Length == 0) break;

                var idx = line.IndexOf(':');
                if (idx <= 0) continue;

                var name = line.Substring(0, idx).Trim().ToLowerInvariant();
                var value = line.Substring(idx + 1).Trim();

                if (name == "content-length")
                {
                    long parsed;
                    if (long.TryParse(value, out parsed)) contentLength = parsed;
                }
                else if (name == "transfer-encoding" &&
                         value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    chunked = true;
                }
            }

            string body;
            if (chunked) body = ReadChunkedBody(stream, deadline);
            else if (contentLength >= 0) body = ReadExact(stream, contentLength, deadline);
            else body = ReadToEnd(stream, deadline);

            cancellationToken.ThrowIfCancellationRequested();

            if (statusCode < 200 || statusCode > 299)
                _logger.Warn("HTTP " + statusCode + " 响应，正文 " + body.Length + " 字符");

            return new HttpResult(statusCode, body, string.Empty);
        }

        private static string ReadAsciiLine(Stream stream)
        {
            using (var buffer = new MemoryStream(128))
            {
                while (true)
                {
                    var b = stream.ReadByte();
                    if (b < 0)
                        return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());

                    if (b == '\n')
                    {
                        var bytes = buffer.ToArray();
                        var len = bytes.Length;
                        if (len > 0 && bytes[len - 1] == '\r') len--;
                        return Encoding.UTF8.GetString(bytes, 0, len);
                    }

                    buffer.WriteByte((byte)b);
                }
            }
        }

        private static string ReadExact(Stream stream, long length, DateTime deadline)
        {
            if (length <= 0) return string.Empty;
            if (length > int.MaxValue) length = int.MaxValue;

            var buffer = new byte[(int)length];
            var offset = 0;
            while (offset < buffer.Length)
            {
                if (DateTime.UtcNow > deadline) break;

                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) break;
                offset += read;
            }
            return Encoding.UTF8.GetString(buffer, 0, offset);
        }

        private static string ReadChunkedBody(Stream stream, DateTime deadline)
        {
            using (var output = new MemoryStream())
            {
                while (true)
                {
                    if (DateTime.UtcNow > deadline) break;

                    var sizeLine = ReadAsciiLine(stream);
                    if (sizeLine == null) break;

                    var semicolon = sizeLine.IndexOf(';');
                    if (semicolon >= 0) sizeLine = sizeLine.Substring(0, semicolon);

                    int size;
                    if (!int.TryParse(sizeLine.Trim(), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out size))
                    {
                        break;
                    }
                    if (size <= 0) break;

                    var chunk = new byte[size];
                    var offset = 0;
                    while (offset < size)
                    {
                        var read = stream.Read(chunk, offset, size - offset);
                        if (read <= 0) break;
                        offset += read;
                    }
                    output.Write(chunk, 0, offset);

                    ReadAsciiLine(stream); // 丢弃 chunk 后面的 CRLF
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        /// <summary>
        /// 没有 Content-Length 也不是 chunked 时，只能读到对端关闭连接。
        /// <para>
        /// 我们不发 <c>Connection: close</c>（风控只放行两个头），所以对端如果保持
        /// keep-alive，这里会一直等到 socket 读超时。为了不把"其实已经拿到完整正文"
        /// 的情况误判成失败，读超时/连接被重置时**返回已经读到的内容**，只记一条警告。
        /// </para>
        /// </summary>
        private string ReadToEnd(Stream stream, DateTime deadline)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                try
                {
                    while (true)
                    {
                        if (DateTime.UtcNow > deadline) break;

                        var read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        output.Write(buffer, 0, read);
                    }
                }
                catch (IOException ex)
                {
                    if (output.Length > 0)
                    {
                        _logger.Warn("连接在读满前中断，使用已收到的 " + output.Length +
                                     " 字节: " + ex.Message);
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (SocketException ex)
                {
                    if (output.Length > 0)
                    {
                        _logger.Warn("连接在读满前中断，使用已收到的 " + output.Length +
                                     " 字节: " + ex.Message);
                    }
                    else
                    {
                        throw;
                    }
                }

                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
