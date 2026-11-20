using System;

namespace KtvDownloader.Controls
{
    /// <summary>
    /// 分页计算。**纯函数**，从 <see cref="PaginationBar"/> 里抽出来以便单元测试
    /// （直接测 Control 需要消息循环，不适合放进单元测试）。
    /// </summary>
    public static class PaginationMath
    {
        /// <summary>
        /// 由总条数与每页条数算总页数。总数未知（&lt;=0）时返回 1，
        /// 这样"下一页/尾页"会保持禁用，而不是让用户跳到一个编出来的页码。
        /// </summary>
        public static int ComputeTotalPages(int totalCount, int pageSize)
        {
            if (pageSize <= 0) return 1;
            if (totalCount <= 0) return 1;
            return Math.Max(1, (totalCount + pageSize - 1) / pageSize);
        }

        /// <summary>把页码夹到 [1, totalPages]。</summary>
        public static int ClampPage(int page, int totalPages)
        {
            if (totalPages < 1) totalPages = 1;
            return Math.Max(1, Math.Min(page, totalPages));
        }

        /// <summary>
        /// 总条数未知时的估算：已取到满页说明后面还有，用 (page+1)*pageSize 让"下一页"可点；
        /// 不满页说明到底了。
        /// </summary>
        public static int EstimateTotalCount(int page, int pageSize, int loadedCount)
        {
            if (pageSize <= 0) return loadedCount;
            var offset = (Math.Max(1, page) - 1) * pageSize;
            return loadedCount < pageSize ? offset + loadedCount : (page + 1) * pageSize;
        }
    }
}
