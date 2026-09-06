using ModularMech.Data;

namespace ModularMech.Tests
{
    /// <summary>
    /// <see cref="ILocomotionProfileData"/> のテスト用 POCO 実装。
    /// </summary>
    public sealed class FakeLocomotionProfileData : ILocomotionProfileData
    {
        public LocomotionType Type { get; set; }
        public float BaseMoveSpeed { get; set; }
        public float BaseTurnSpeed { get; set; }
        public float BaseJumpPower { get; set; }
        public float Acceleration { get; set; }
        public float GroundOffset { get; set; }

        /// <summary>0 以下は「容量未設定」= 過積載判定を行わない(設計ドキュメント §3.3 のゼロ除算既定)。</summary>
        public float WeightCapacity { get; set; }
    }
}
