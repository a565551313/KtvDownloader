using System;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace KtvDownloader.Infrastructure.Security
{
    /// <summary>
    /// RSA 公钥加密（PKCS#1 v1.5）。
    /// <para>
    /// 曲库登录接口要求标准 PKCS#1 v1.5 填充，
    /// 而 .NET Framework 的 <see cref="RSA"/> 只提供裸模幂，
    /// 所以这里手工实现填充与模幂运算。
    /// </para>
    /// <para>
    /// 注意：.NET Framework 4.8 的 <see cref="BigInteger"/> 不支持
    /// <c>isUnsigned</c> / <c>isBigEndian</c> 构造函数（那是 .NET Core 的 API），
    /// 因此本类自带大端/小端与无符号转换辅助方法。
    /// </para>
    /// </summary>
    public static class RsaPkcs1Encryptor
    {
        /// <summary>
        /// 用 Base64 编码的 X.509 SubjectPublicKeyInfo 公钥加密明文，返回 Base64 密文。
        /// </summary>
        public static string EncryptToBase64(string publicKeyBase64, string plaintext)
        {
            if (string.IsNullOrWhiteSpace(publicKeyBase64))
                throw new ArgumentException("公钥为空", nameof(publicKeyBase64));
            if (plaintext == null)
                throw new ArgumentNullException(nameof(plaintext));

            var der = Convert.FromBase64String(NormalizeBase64(publicKeyBase64));

            BigInteger modulus;
            BigInteger exponent;
            ParseSubjectPublicKeyInfo(der, out modulus, out exponent);

            var keyBytes = GetByteLength(modulus);
            var message = Encoding.UTF8.GetBytes(plaintext);

            if (message.Length > keyBytes - 11)
                throw new ArgumentException("明文过长，超出 RSA 可加密长度");

            // EM = 0x00 || 0x02 || PS(随机非零) || 0x00 || M
            var paddingLength = keyBytes - message.Length - 3;
            var block = new byte[keyBytes];
            block[0] = 0x00;
            block[1] = 0x02;
            FillNonZeroRandom(block, 2, paddingLength);
            block[2 + paddingLength] = 0x00;
            Buffer.BlockCopy(message, 0, block, 3 + paddingLength, message.Length);

            var value = FromBigEndianUnsigned(block);
            var cipher = BigInteger.ModPow(value, exponent, modulus);

            return Convert.ToBase64String(ToBigEndianUnsigned(cipher, keyBytes));
        }

        #region 大端无符号转换

        /// <summary>把大端无符号字节数组转成 BigInteger。</summary>
        private static BigInteger FromBigEndianUnsigned(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return BigInteger.Zero;

            // BigInteger 的 byte[] 构造函数按小端、有符号解释。
            // 末尾补一个 0x00 保证最高位为 0，避免被当成负数。
            var littleEndian = new byte[bytes.Length + 1];
            for (var i = 0; i < bytes.Length; i++)
                littleEndian[i] = bytes[bytes.Length - 1 - i];
            littleEndian[bytes.Length] = 0x00;

            return new BigInteger(littleEndian);
        }

        /// <summary>把 BigInteger 转成固定长度的大端无符号字节数组。</summary>
        private static byte[] ToBigEndianUnsigned(BigInteger value, int length)
        {
            var littleEndian = value.ToByteArray();
            var result = new byte[length];

            // 从末尾开始反向拷贝，多余的高位零字节自动丢弃。
            var count = Math.Min(littleEndian.Length, length);
            for (var i = 0; i < count; i++)
                result[length - 1 - i] = littleEndian[i];

            return result;
        }

        /// <summary>计算 BigInteger 占用多少字节（向上取整）。</summary>
        private static int GetByteLength(BigInteger value)
        {
            if (value.Sign <= 0) return 1;
            return (value.GetBitLengthCompat() + 7) / 8;
        }

        /// <summary>
        /// 取位长度。.NET Framework 没有 GetBitLength()，
        /// 这里用 ToByteArray 的长度估算（足够用于 RSA 定长计算）。
        /// </summary>
        private static int GetBitLengthCompat(this BigInteger value)
        {
            if (value.Sign <= 0) return 0;

            var bytes = value.ToByteArray();
            var length = bytes.Length;

            // ToByteArray 可能带一个 0x00 符号位字节，去掉它再算。
            if (length > 1 && bytes[length - 1] == 0x00) length--;

            var bits = (length - 1) * 8;
            var topByte = bytes[length - 1];
            while (topByte > 0)
            {
                bits++;
                topByte >>= 1;
            }
            return bits;
        }

        #endregion

        private static void FillNonZeroRandom(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return;

            var random = new byte[count];
            var filled = 0;
            using (var rng = RandomNumberGenerator.Create())
            {
                while (filled < count)
                {
                    rng.GetBytes(random);
                    for (var i = 0; i < random.Length && filled < count; i++)
                    {
                        // PKCS#1 要求填充字节非零。
                        if (random[i] == 0) continue;
                        buffer[offset + filled] = random[i];
                        filled++;
                    }
                }
            }
        }

        private static string NormalizeBase64(string value)
        {
            return value.Replace("\\", string.Empty)
                        .Replace("\r", string.Empty)
                        .Replace("\n", string.Empty)
                        .Replace(" ", string.Empty);
        }

        /// <summary>
        /// 解析 X.509 SubjectPublicKeyInfo，取出 RSA 的模数与指数。
        /// </summary>
        private static void ParseSubjectPublicKeyInfo(byte[] der, out BigInteger modulus, out BigInteger exponent)
        {
            var index = 0;

            // 外层 SEQUENCE
            ReadTag(der, ref index, 0x30);
            ReadLength(der, ref index);

            // AlgorithmIdentifier（整段跳过）
            ReadTag(der, ref index, 0x30);
            var algorithmLength = ReadLength(der, ref index);
            index += algorithmLength;

            // BIT STRING
            ReadTag(der, ref index, 0x03);
            ReadLength(der, ref index);
            index++; // 跳过 unused-bits 字节

            // 内层 RSAPublicKey SEQUENCE
            ReadTag(der, ref index, 0x30);
            ReadLength(der, ref index);

            // INTEGER modulus
            ReadTag(der, ref index, 0x02);
            var modulusLength = ReadLength(der, ref index);
            modulus = FromBigEndianUnsigned(Slice(der, index, modulusLength));
            index += modulusLength;

            // INTEGER exponent
            ReadTag(der, ref index, 0x02);
            var exponentLength = ReadLength(der, ref index);
            exponent = FromBigEndianUnsigned(Slice(der, index, exponentLength));
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > source.Length)
                throw new CryptographicException("DER 数据越界");

            var result = new byte[length];
            Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }

        private static void ReadTag(byte[] der, ref int index, byte expectedTag)
        {
            if (index >= der.Length) throw new CryptographicException("DER 数据不完整");
            if (der[index] != expectedTag)
                throw new CryptographicException("DER 标签不匹配，期望 0x" + expectedTag.ToString("X2"));
            index++;
        }

        private static int ReadLength(byte[] der, ref int index)
        {
            if (index >= der.Length) throw new CryptographicException("DER 长度字段缺失");

            var first = der[index++];
            if ((first & 0x80) == 0) return first;

            var byteCount = first & 0x7F;
            if (byteCount == 0 || byteCount > 4) throw new CryptographicException("DER 长度格式不支持");

            var length = 0;
            for (var i = 0; i < byteCount; i++)
            {
                if (index >= der.Length) throw new CryptographicException("DER 长度字段不完整");
                length = (length << 8) | der[index++];
            }
            return length;
        }
    }
}
