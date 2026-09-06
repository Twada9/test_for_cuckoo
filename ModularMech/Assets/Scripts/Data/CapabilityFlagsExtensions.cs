namespace ModularMech.Data
{
    public static class CapabilityFlagsExtensions
    {
        /// <summary>
        /// フラグ判定。<c>Enum.HasFlag</c> はボックス化を伴い毎フレームの呼び出しで GC を生むため、
        /// プロジェクト全体でこちらを使う。
        /// </summary>
        public static bool Has(this CapabilityFlags value, CapabilityFlags flag)
        {
            return (value & flag) == flag;
        }
    }
}
