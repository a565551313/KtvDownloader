using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// ⚠️ 这里**不要**再写 AssemblyTitle / AssemblyDescription / AssemblyProduct /
//    AssemblyVersion / AssemblyFileVersion / AssemblyConfiguration 之类的特性。
//
// 工程已经打开 <GenerateAssemblyInfo>true</GenerateAssemblyInfo>，
// 这些特性由 SDK 依据 csproj 里的 <Version> / <AssemblyVersion> / <Product> /
// <Description> 自动生成（生成到 obj\...\AssemblyInfo.cs）。
// 两边都写会撞出 CS0579「重复的特性」，整个工程编译不过。
//
// 版本号只在 csproj 里维护一处：
//   - 启动日志与「关于」对话框读的就是它（AssemblyInfoHelper.GetVersion）；
//   - CI 可以用 -p:Version=x.y.z 覆盖，于是日志里能区分出是哪个构建；
//   - 早期这里被钉死成 1.0.0.0，改了多少版都看不出来。

[assembly: ComVisible(false)]
[assembly: Guid("8f3c1d92-5b47-4a8e-9c62-1f4d8a7b3e50")]

// 单元测试需要摸到几处 internal 的测试钩子：
//   - TsDecryptService 的 #region 测试钩子（签名、密钥、分段规则）
//   - PreviewService.EstimateBytes / BuildCacheKey
//   - SingerNameLookup（internal 类）
// 不放开这些，测试就只能去连真曲库、真源站，既慢又不稳定。
[assembly: InternalsVisibleTo("KtvDownloader.Tests")]
