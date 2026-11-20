using System;
using System.Text;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 加密 TS 的识别与分段规则。
    /// <para>
    /// 这里钉住的是一个影响面很大的回归：早期判据是"首字节不是 0x47 就当加密"，
    /// 而曲库里还有 1,157 首 <c>.mp3</c> 与 8,488 首 <c>.ls/.kls</c>（docs §5.4），
    /// 它们首字节当然不是 TS 同步字节 → 被送去解密 → 签名不匹配 → 整首歌判"下载失败"。
    /// </para>
    /// </summary>
    [TestClass]
    public class TsDecryptServiceTests
    {
        /// <summary>造一个 512 字节私有头，签名写在 [500..512]。</summary>
        private static byte[] BuildHeader(byte[] signature)
        {
            var header = new byte[TsDecryptService.PrivateHeaderSize];

            // 前面填一点"看起来像 TS"的字节，证明判据**不看**首字节。
            for (var i = 0; i < 500; i++) header[i] = (byte)(i % 251);

            if (signature != null)
                Buffer.BlockCopy(signature, 0, header, 500, signature.Length);

            return header;
        }

        [TestMethod]
        public void 已知签名_判定为加密()
        {
            using (var temp = new TempDirectory())
            {
                foreach (var signature in new[]
                {
                    TsDecryptService.ThunderSignature,
                    TsDecryptService.Hhc1Signature,
                    TsDecryptService.Hhc2Signature
                })
                {
                    var path = temp.WriteFile(
                        "enc-" + Encoding.ASCII.GetString(signature) + ".ts",
                        BuildHeader(signature));

                    Assert.IsTrue(TsDecryptService.IsEncrypted(path),
                        Encoding.ASCII.GetString(signature) + " 应该被识别为加密");
                }
            }
        }

        [TestMethod]
        public void 首字节不是0x47但没有已知签名_不判为加密()
        {
            using (var temp = new TempDirectory())
            {
                // 这就是 mp3 的形态：ID3 开头，首字节不是 0x47，也不是我们的加密文件。
                var mp3 = new byte[TsDecryptService.PrivateHeaderSize + 64];
                mp3[0] = (byte)'I';
                mp3[1] = (byte)'D';
                mp3[2] = (byte)'3';

                var path = temp.WriteFile("song.mp3", mp3);

                Assert.IsFalse(TsDecryptService.IsEncrypted(path),
                    "mp3 不该被误判为加密 TS，否则整首歌会被判下载失败");
            }
        }

        [TestMethod]
        public void 签名位置不对_不判为加密()
        {
            using (var temp = new TempDirectory())
            {
                // 签名写在开头而不是 [500..512]。
                var buffer = new byte[TsDecryptService.PrivateHeaderSize];
                Buffer.BlockCopy(TsDecryptService.ThunderSignature, 0, buffer, 0, 12);

                var path = temp.WriteFile("fake.ts", buffer);

                Assert.IsFalse(TsDecryptService.IsEncrypted(path));
            }
        }

        [TestMethod]
        public void 文件不足512字节_不判为加密()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.WriteFile("tiny.ts", new byte[] { 1, 2, 3 });

                Assert.IsFalse(TsDecryptService.IsEncrypted(path));
            }
        }

        [TestMethod]
        public void 路径为空或文件不存在_不抛异常()
        {
            Assert.IsFalse(TsDecryptService.IsEncrypted(null));
            Assert.IsFalse(TsDecryptService.IsEncrypted("   "));
            Assert.IsFalse(TsDecryptService.IsEncrypted(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "ktvdl-不存在-" + Guid.NewGuid().ToString("N") + ".ts")));
        }

        [TestMethod]
        public void 密钥是32字节也就是AES256()
        {
            // README 与 docs 早期写的是 AES-128，与实现不符：密钥表里每一项都是 64 个十六进制字符 = 32 字节。
            foreach (var signature in new[]
            {
                TsDecryptService.ThunderSignature,
                TsDecryptService.Hhc1Signature,
                TsDecryptService.Hhc2Signature
            })
            {
                for (var index = 0; index < 16; index++)
                {
                    var key = TsDecryptService.GetKey(signature, index);

                    Assert.IsNotNull(key);
                    Assert.AreEqual(32, key.Length, "AES-256 的密钥必须是 32 字节");
                }
            }
        }

        [TestMethod]
        public void 未知签名_取不到密钥()
        {
            Assert.IsNull(TsDecryptService.GetKey(Encoding.ASCII.GetBytes("UNKNOWN_SIG!"), 0));
        }

        [TestMethod]
        public void 密钥索引_按低4位取模()
        {
            var thunder = TsDecryptService.ThunderSignature;

            Assert.AreEqual(
                Convert.ToBase64String(TsDecryptService.GetKey(thunder, 3)),
                Convert.ToBase64String(TsDecryptService.GetKey(thunder, 3 + 16)));
        }

        [TestMethod]
        public void 分段加密_间隔为0表示全加密()
        {
            for (var i = 0; i < 10; i++)
                Assert.IsTrue(TsDecryptService.ShouldDecrypt(i, 0, 0));
        }

        [TestMethod]
        public void 分段加密_首段一定加密()
        {
            Assert.IsTrue(TsDecryptService.ShouldDecrypt(7, 7, 4));
        }

        [TestMethod]
        public void 分段加密_首段之后按间隔取模()
        {
            const int first = 0;
            const int interval = 4;

            Assert.IsTrue(TsDecryptService.ShouldDecrypt(0, first, interval));
            Assert.IsTrue(TsDecryptService.ShouldDecrypt(1, first, interval));   // 1 % 4 == 1
            Assert.IsFalse(TsDecryptService.ShouldDecrypt(2, first, interval));
            Assert.IsFalse(TsDecryptService.ShouldDecrypt(3, first, interval));
            Assert.IsFalse(TsDecryptService.ShouldDecrypt(4, first, interval));
            Assert.IsTrue(TsDecryptService.ShouldDecrypt(5, first, interval));   // 5 % 4 == 1
            Assert.IsTrue(TsDecryptService.ShouldDecrypt(9, first, interval));   // 9 % 4 == 1
        }

        [TestMethod]
        public void 分段加密_首段之前不解密()
        {
            Assert.IsFalse(TsDecryptService.ShouldDecrypt(3, 10, 4));
        }
    }
}
