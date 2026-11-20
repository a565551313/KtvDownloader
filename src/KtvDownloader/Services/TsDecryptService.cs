using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// Thunder / HHC 加密 TS 解密服务。
    /// <para>
    /// 加密格式（前 512 字节私有头）：
    /// <list type="bullet">
    /// <item>头[500..512]：签名，THUNDERCRYP3 / HHCMUSECRYP1 / HHCMUSECRYP2</item>
    /// <item>头[53] &amp; 0x0F：密钥索引</item>
    /// <item>头[452]：段大小（KB）</item>
    /// <item>头[453]：模式，仅支持 0</item>
    /// <item>头[454]：间隔，0 表示每段都加密</item>
    /// <item>头[457]：第一个加密段号</item>
    /// </list>
    /// 解密条件：interval==0 则全解；否则 seg==first 或 (seg&gt;first 且 seg%interval==1)。
    /// 每段用 AES-ECB/NoPadding 解前 (len - len%16) 字节，尾部原样保留。
    /// </para>
    /// <para>
    /// ⚠️ 密钥长度：内置密钥表每项是 **64 个十六进制字符 = 32 字节**，
    /// 所以实际是 **AES-256**-ECB，不是早期文档写的 AES-128。
    /// 照 AES-128 重写实现会直接抛 <c>CryptographicException</c>（密钥长度非法）。
    /// </para>
    /// </summary>
    public sealed class TsDecryptService : ITsDecryptService
    {
        private const int HeaderSize = 512;
        private const int TsPacketSize = 188;

        /// <summary>签名在私有头里的偏移。</summary>
        private const int SignatureOffset = 500;

        /// <summary>签名长度（THUNDERCRYP3 等均为 12 字节）。</summary>
        private const int SignatureLength = 12;

        /// <summary>AES 分组大小。</summary>
        private const int AesBlockSize = 16;

        private readonly ILogger _logger;

        public TsDecryptService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private static readonly byte[] SignatureThunder = ToAscii("THUNDERCRYP3");
        private static readonly byte[] SignatureHhc1 = ToAscii("HHCMUSECRYP1");
        private static readonly byte[] SignatureHhc2 = ToAscii("HHCMUSECRYP2");

        /// <summary>Thunder 密钥表（16 组，索引来自 头[53] &amp; 0x0F）。</summary>
        private static readonly string[] ThunderKeys =
        {
            "c6d3cdd0f1ebf5ded4d7d3cebbd3dad2cecdd0b8deaccbead2c5cac3c2dba6ac",
            "f5deebd8d3ced7b9dad2d3d8d0bcd9f6cbd7c9c2cab1d0f0a6f8d0afd0d3b9d4",
            "c3d4d7b2c2d5aaeecbd3b7dcbcc6b4f4b7d4b2b2fed5e0cbd3d5b2d7c6b7cef3",
            "b6bdaea7ced2d2b4aac1bbd3cbc8b7d6aacbbdaecbd4cbb5f9daddc0cecbe4d7",
            "d7d3d4dde8cedaf3c7b4cbddd2d3aee7f5d6d6b2d2aed0c9cbcddbb2ddf0e6c9",
            "b4d3d3b0d9d6cbe2e2aeaea2d6b8c7b2c3c9e5bbd6d9c7bcaee2d2dababac1b2",
            "d9d6cbe2e2aeaea2d6b2c7b2c3e0e5bbd6d9c7bcaee2d2dababad6b2d3d3b1bb",
            "e2aeaea2d69dc7b2c35fe5bbd6d9c7bcaee2d2dababac2b2d3d3d9bbd6cbe2f0",
            "97bbecd2fab782f0c1d0f6b3bab2c8a9dec7d0ec62fdf8b1d3d2cebccef5c4dd",
            "eadcd4eecad6d3bec7ae81fde6badcd7eecf98d3c1f6d1cefac8d4c2b6d2c4c6",
            "e7bed4c1b5fdd8bcbfd7d0c867d3cbcbd1d4d1d6d4d8e1c8c4c7d1d6eedee1c8",
            "f2b1c8bdbbd4cfeff1b0d0f0d3cbddd4cec492d8d3c2b3e1daedb5fdb1bcf0d0",
            "d3cdd2d6d2f5d1dc81c1b3ba90eec2ee81cecece90c5fdc4ceb2d4cdc4bbd5f5",
            "f5cbaee0b9c9e8cafafae5bfcdcebccef5acc3c4b9d6bccdfadcc3f5bfd6b6d2",
            "d6d3c3b7dcdad2f4b7d6b3c3fedca3f4baccd2c2eeecf3e3b7c3cabdfefcbfab",
            "e0b4e4f8b8c9c9d3a3a5cfdad2cab5d2f3a6dbf3d6bfd2bfaecbcba5cec5bcc3"
        };

        /// <summary>HHC1 密钥表。</summary>
        private static readonly string[] Hhc1Keys =
        {
            "d7cad2d3d3b1e0d0d4cfcbc5bbb0b5f3d1d6d4d7a7aec3d4b6b2bad4f8bbf5b6",
            "d3cfd2d0b2b6d5d2bbf8dfb2babacebec3c3b4fdb7d7d6d7b8f7aed3c9c2d3ce",
            "cec4babde1b1f5bbc9b6d3b6edf8ebf8ceb2c5b2aabbf3bbc8d6d3d0cbd2d1c5",
            "f6f8dad0d4d0b6d3f2c5f8d0b5b7c7d3dcbad7e0bdb0c8c1f7aecaa6b6d6d0d4",
            "d4b1d2d7bbd8d3d3ceced7b2b4bdd3bbd1d6d4d6a7aebbd8ced1bed4e1a7fdf2",
            "d3d3d3eec7b9d6d2ddb1c1b2ced4d3b1cabbdad8d3b7cacedaf2c7c5d7d7b0c6",
            "d6d7b9c3aed3dbbbc7d4c6b9f3bbe4dbd6b8d6c6aeb8bee4d3d4b8d0ebdab8d0",
            "d0aabbaecbbad2d2f9cdd4e0b2b6c0b2bbf8f1bbd0babdbfd0cddac9d6b2d6d0",
            "d7b1b0b6d3a5b2f8cabec3c9b3d3f4f7ceced3d3dededadac7c7cad1f3f3c2d4",
            "f8c3d3c6c0c0b9c8d6f1b1e7b8d5d4c7bbdfbbd0b6d2cac8f8b2abe7bad7d4b4",
            "d6bbd2d5aebcb2feb2b2d7d2bbbbd3d4bcd6d4b5baaabbc2d6c8cec6aacbaaa9",
            "aee2c0ebd2b6d6d6d4f8aeaed0ced2d2ccded4d4c3b3b5c0f1dcc2f1c3b5c6d3",
            "cbb4b2dcb3d3bbb2c6d0d3d7dfc4e2d3cacbbeceaef9d8cab6d3c3d0f8fbcfa2",
            "aee1c0cfd2d6d6ced4aeaee4c0d2d2b2f1d4d4aecbc0c0cec0f1f1cad4bcc3d0",
            "bebad0c4b4f5a2d1bad7d7d3ced3d3d0d2cfd4cad4c4bbc2b1cec9b5f0caabdc",
            "bda2dee4d2bbd7cbe0d8d3f9d7d2d4d2e3b2bbd4d2b2cab9d4bbd3dbb7d3c6c6"
        };

        /// <summary>HHC2 密钥表。</summary>
        private static readonly string[] Hhc2Keys =
        {
            "b1b4d6d6f8f3aeaed5cab5b5dfc2d8c0b9cbb4b2fac0e6bbd6c9cdbfaefaf6c9",
            "c0dac9c9d5c9d3d3dfcfebebc1cdd6d6eeacaeaec3d2cbc9f1e2c0fad3bfbfb6",
            "d5b5b7bddfc0b2abc7d6b4c4faf7cbaad6d3ceb2c6c3e5bbb9d2d5ced9b2dfc5",
            "a8f8bfcdc1d6d7b7eedae4a3cacacacaebebebebd0c7c1c3d0bfb7f7b1cac9ce",
            "ced7d5d6aaf4dfc6d6c6d2c8aee4f2a8cacdc0d2c6e2fbb2d2cab6b1d4c6f8f8",
            "f8f8f8f8c8b1b1c4a1b8dcd3d6d6d6d6aeaeaeaecac7c5b1b5bfadb0b6b6b6b6",
            "c3b5cecbedc3b4e3cbcbd5b2e3e3bdbbcab6b6caa4e0f8a4d5d2c3d5dfb2eddf",
            "e1cbf2b8b8b4c7d4eff8a7f2b3bcc0c4b5d7efdac7cac0cda7aea1e2b3cdc1d6",
            "b6b9c7d4dba5fcf2b1b3beb9f8c7c3fab4d4b1d3ecf2a9c3c8c1cab2f1a6a6bb",
            "d9aef8fbcebebed5b4c3c3dfb6d2b6cec3b2f8b4c7b7b9d6c9f2faaed6b1c0d3",
            "c8d2b9d7a1f2cae3d3c1bed2c3b8fcb2d3d3cab9dadab3fab9b5bfd6fad0c9ae",
            "a6dad9aec7d0d0c8fce9d5a5d6d3d6c6d0daaee4d4bcb7c6add2d1dfc4b0cab9",
            "d2b6b8cebbfed1e1d6cad2b6d3aebbfeb5d6cacab1d3afaecebdb5cae1d9b1af",
            "d3e4aed0b6c9cab6f8c6c7f8b3b6ced2cbf8bde6d6d1cac7aef8a4bfd7d6b5b9",
            "b9bebec2fafcfcc3b4ceb4ceceaaceaad6c9d6c9aecfaecfc8c6c8c6abc6abc6",
            "f8f8dff8c7c9d2b7fcc6b2a5c8d6b9c4cbaecab1d6c9c9c6aec6cfe4b1d5b1b4"
        };

        public Task<bool> IsEncryptedAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => IsEncrypted(filePath), cancellationToken);
        }

        /// <summary>
        /// 判断是否为我们**能解密**的加密 TS。
        /// <para>
        /// 判据只有一个：头[500..512] 命中已知签名（THUNDERCRYP3 / HHCMUSECRYP1 / HHCMUSECRYP2）。
        /// </para>
        /// <para>
        /// ⚠️ 不要用"首字节不是 0x47 就当加密"：曲库里还有 1,157 首 <c>.mp3</c> 与
        /// 8,488 首 <c>.ls/.kls</c>（docs §5.4），它们的首字节当然不是 TS 同步字节，
        /// 一旦被误判为加密，后续解密必然因签名不匹配失败，整首歌就被判成"下载失败"。
        /// 非 TS 内容一律原样透传，交给后面的 ffmpeg / 播放器处理。
        /// </para>
        /// </summary>
        public static bool IsEncrypted(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < HeaderSize) return false;

                    var header = new byte[HeaderSize];
                    if (!TryReadExactly(stream, header, HeaderSize)) return false;

                    return IsKnownSignature(ExtractSignature(header));
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>取头[500..512] 的 12 字节签名。</summary>
        private static byte[] ExtractSignature(byte[] header)
        {
            var signature = new byte[SignatureLength];
            Buffer.BlockCopy(header, SignatureOffset, signature, 0, SignatureLength);
            return signature;
        }

        public Task<bool> DecryptAsync(
            string sourcePath,
            string outputPath,
            IProgress<double> progress = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => Decrypt(sourcePath, outputPath, progress, cancellationToken), cancellationToken);
        }

        private bool Decrypt(
            string sourcePath,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(sourcePath))
            {
                _logger.Warn("待解密文件不存在: " + sourcePath);
                return false;
            }

            try
            {
                using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 256 * 1024))
                {
                    if (input.Length <= HeaderSize)
                    {
                        _logger.Warn("文件过小，不是加密 TS: " + sourcePath);
                        return false;
                    }

                    var header = new byte[HeaderSize];
                    if (!TryReadExactly(input, header, HeaderSize))
                    {
                        _logger.Warn("私有头读取不完整，不是加密 TS: " + sourcePath);
                        return false;
                    }

                    var signature = ExtractSignature(header);

                    var keyTable = ResolveKeyTable(signature);
                    if (keyTable == null)
                    {
                        _logger.Warn("未知加密签名，无法解密: " + DescribeSignature(signature));
                        return false;
                    }

                    var keyIndex = header[53] & 0x0F;
                    var key = HexToBytes(keyTable[keyIndex]);
                    var segmentSize = header[452] * 1024;
                    var mode = header[453];
                    var interval = header[454];
                    var firstEncryptedSegment = header[457];

                    if (segmentSize <= 0)
                    {
                        _logger.Warn("加密段大小为 0，无法解密");
                        return false;
                    }

                    if (mode != 0)
                    {
                        _logger.Warn("不支持的 Thunder 加密模式: " + mode);
                        return false;
                    }

                    var totalBytes = input.Length - HeaderSize;
                    var processed = 0L;

                    using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 256 * 1024))
                    using (var aes = Aes.Create())
                    {
                        aes.Key = key;
                        aes.Mode = CipherMode.ECB;
                        aes.Padding = PaddingMode.None;

                        using (var decryptor = aes.CreateDecryptor())
                        {
                            var segment = new byte[segmentSize];
                            long segmentIndex = 0;
                            var lastReportedPercent = -1;

                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                var count = ReadChunk(input, segment);
                                if (count <= 0) break;

                                if (ShouldDecrypt(segmentIndex, firstEncryptedSegment, interval))
                                {
                                    var encryptedLength = count - (count % AesBlockSize);
                                    if (encryptedLength > 0)
                                    {
                                        // ECB 无状态，可以反复用同一个 transform 逐段处理。
                                        var decrypted = new byte[encryptedLength];
                                        decryptor.TransformBlock(segment, 0, encryptedLength,
                                            decrypted, 0);

                                        output.Write(decrypted, 0, decrypted.Length);
                                        if (encryptedLength < count)
                                            output.Write(segment, encryptedLength, count - encryptedLength);
                                    }
                                    else
                                    {
                                        output.Write(segment, 0, count);
                                    }
                                }
                                else
                                {
                                    output.Write(segment, 0, count);
                                }

                                processed += count;
                                segmentIndex++;

                                if (progress != null && totalBytes > 0)
                                {
                                    var percent = (int)(processed * 100 / totalBytes);
                                    if (percent != lastReportedPercent)
                                    {
                                        lastReportedPercent = percent;
                                        progress.Report(percent);
                                    }
                                }
                            }
                        }
                    }

                    var expectedLength = input.Length - HeaderSize;
                    var actualLength = new FileInfo(outputPath).Length;
                    if (actualLength != expectedLength)
                    {
                        _logger.Warn("解密后长度不符 期望=" + expectedLength + " 实际=" + actualLength);
                        return false;
                    }

                    if (!IsValidTransportStream(outputPath))
                    {
                        _logger.Warn("解密后不是合法 TS: " + outputPath);
                        return false;
                    }

                    _logger.Info("TS 解密完成 密钥索引=" + keyIndex + " 段大小=" + segmentSize +
                                 " 间隔=" + interval + " 首段=" + firstEncryptedSegment);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                SafeDelete(outputPath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("TS 解密失败: " + sourcePath, ex);
                SafeDelete(outputPath);
                return false;
            }
        }

        /// <summary>
        /// 该段是否需要解密。
        /// interval==0 → 每段都加密；否则命中首段，或 (seg&gt;first 且 seg%interval==1)。
        /// </summary>
        internal static bool ShouldDecrypt(long segmentIndex, int firstEncryptedSegment, int interval)
        {
            if (interval == 0) return true;
            if (segmentIndex == firstEncryptedSegment) return true;
            return segmentIndex > firstEncryptedSegment && segmentIndex % interval == 1;
        }

        private static string[] ResolveKeyTable(byte[] signature)
        {
            if (BytesEqual(signature, SignatureThunder)) return ThunderKeys;
            if (BytesEqual(signature, SignatureHhc1)) return Hhc1Keys;
            if (BytesEqual(signature, SignatureHhc2)) return Hhc2Keys;
            return null;
        }

        private static bool IsKnownSignature(byte[] signature)
        {
            return ResolveKeyTable(signature) != null;
        }

        private static string DescribeSignature(byte[] signature)
        {
            var builder = new System.Text.StringBuilder(signature.Length);
            foreach (var b in signature)
            {
                if (b >= 32 && b < 127) builder.Append((char)b);
                else builder.Append('.');
            }
            return builder.ToString();
        }

        /// <summary>校验解密结果是否为合法 TS：连续若干包的同步字节都是 0x47。</summary>
        private static bool IsValidTransportStream(string filePath)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var packetCount = Math.Min(stream.Length / TsPacketSize, 4096);
                    if (packetCount <= 0) return false;

                    var packet = new byte[TsPacketSize];
                    for (var i = 0; i < packetCount; i++)
                    {
                        if (ReadChunk(stream, packet) < TsPacketSize) return false;
                        if (packet[0] != 0x47) return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读满 count 字节。返回是否真的读满 —— 调用方**必须**检查返回值，
        /// 否则短读时会拿着半截全零的私有头继续解析，得出荒谬的段大小/密钥索引。
        /// </summary>
        private static bool TryReadExactly(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) break;
                offset += read;
            }
            return offset == count;
        }

        /// <summary>尽量填满缓冲区，返回实际读到的字节数。</summary>
        private static int ReadChunk(Stream stream, byte[] buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }
            return true;
        }

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
                throw new ArgumentException("十六进制字符串非法");

            var result = new byte[hex.Length / 2];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = byte.Parse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            return result;
        }

        private static byte[] ToAscii(string value)
        {
            var bytes = new byte[value.Length];
            for (var i = 0; i < value.Length; i++) bytes[i] = (byte)value[i];
            return bytes;
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 忽略。
            }
        }

        #region 测试钩子

        // 以下成员只给单元测试用（配合 AssemblyInfo 里的 InternalsVisibleTo）。
        // 有了它们，测试可以自己构造一个"512 字节私有头 + AES 分段加密"的样本文件，
        // 跑完整的解密回环，而不需要真的去源站下一首歌。

        /// <summary>私有头长度（512）。</summary>
        internal const int PrivateHeaderSize = HeaderSize;

        /// <summary>Thunder 签名的副本。</summary>
        internal static byte[] ThunderSignature
        {
            get { return (byte[])SignatureThunder.Clone(); }
        }

        /// <summary>HHC1 签名的副本。</summary>
        internal static byte[] Hhc1Signature
        {
            get { return (byte[])SignatureHhc1.Clone(); }
        }

        /// <summary>HHC2 签名的副本。</summary>
        internal static byte[] Hhc2Signature
        {
            get { return (byte[])SignatureHhc2.Clone(); }
        }

        /// <summary>
        /// 按签名与密钥索引取 AES 密钥。返回 32 字节（AES-256）；签名不认识时返回 null。
        /// </summary>
        internal static byte[] GetKey(byte[] signature, int keyIndex)
        {
            var table = ResolveKeyTable(signature);
            if (table == null) return null;
            return HexToBytes(table[keyIndex & 0x0F]);
        }

        #endregion
    }
}
