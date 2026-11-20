using System;
using System.Threading;
using System.Threading.Tasks;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// Thunder / HHC 加密 TS 解密服务。
    /// <para>
    /// 源站下发的 .ts 前 512 字节是私有头，头[500..512] 为签名
    /// （THUNDERCRYP3 / HHCMUSECRYP1 / HHCMUSECRYP2）。
    /// 密钥按 头[53] &amp; 0x0F 从内置表取，分段用 AES-128-ECB 解密。
    /// </para>
    /// </summary>
    public interface ITsDecryptService
    {
        /// <summary>判断文件是否为加密 TS。</summary>
        Task<bool> IsEncryptedAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 解密到目标文件。返回是否成功且解密后是合法 TS。
        /// </summary>
        Task<bool> DecryptAsync(
            string sourcePath,
            string outputPath,
            IProgress<double> progress = null,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
