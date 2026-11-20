using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
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
    /// 不写任何其它头。这正是官方 App 抓包看到的形态，也是唯一能通过源站风控的形态。
    /// </para>
    /// </summary>
    public sealed class CleanHttpClient : ICleanHttpClient
    {
        private readonly ILogger _logger;

        public CleanHttpClient(ILogger logger)
        {
            _logger = logger;
        }

        public Task<HttpResult> GetAsync(string url, int timeoutMs = 10000, CancellationToken cancellationToken = default(CancellationToken))
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

            var connectTimeout = Math.Max(1000, Math.Min(timeoutMs, 30000));
            Socket socket = null;
            try
            {
                var port = uri.Port > 0 ? uri.Port : (isHttps ? 443 : 80);
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.ReceiveTimeout = connectTimeout;
                socket.SendTimeout = connectTimeout;
                socket.Connect(uri.Host, port);

                Stream stream = new NetworkStream(socket, false);
                if (isHttps)
                {
                    var ssl = new SslStream(stream, false, (sender, cert, chain, errors) => true);
                    ssl.AuthenticateAsClient(uri.Host);
                    stream = ssl;
                }

                cancellationToken.ThrowIfCancellationRequested();

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

                var result = ReadResponse(stream);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("HTTP 请求失败 " + url + " : " + ex.GetType().Name + " " + ex.Message);
                return HttpResult.Fail(ex.Message);
            }
            finally
            {
                if (socket != null)
                {
                    try { socket.Close(); } catch { }
                    socket.Dispose();
                }
            }
        }

        private static HttpResult ReadResponse(Stream stream)
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
                else if (name == "transfer-encoding" && value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    chunked = true;
                }
            }

            string body;
            if (chunked) body = ReadChunkedBody(stream);
            else if (contentLength >= 0) body = ReadExact(stream, contentLength);
            else body = ReadToEnd(stream);

            return new HttpResult(statusCode, body, string.Empty);
        }

        private static string ReadAsciiLine(Stream stream)
        {
            var buffer = new MemoryStream(128);
            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0) return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
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

        private static string ReadExact(Stream stream, long length)
        {
            if (length <= 0) return string.Empty;
            if (length > int.MaxValue) length = int.MaxValue;
            var buffer = new byte[(int)length];
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) break;
                offset += read;
            }
            return Encoding.UTF8.GetString(buffer, 0, offset);
        }

        private static string ReadChunkedBody(Stream stream)
        {
            var output = new MemoryStream();
            while (true)
            {
                var sizeLine = ReadAsciiLine(stream);
                if (sizeLine == null) break;

                var semicolon = sizeLine.IndexOf(';');
                if (semicolon >= 0) sizeLine = sizeLine.Substring(0, semicolon);

                int size;
                if (!int.TryParse(sizeLine.Trim(), System.Globalization.NumberStyles.HexNumber, null, out size))
                    break;
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

        private static string ReadToEnd(Stream stream)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    output.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
