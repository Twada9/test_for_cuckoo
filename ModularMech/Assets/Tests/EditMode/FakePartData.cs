using ModularMech.Data;

namespace ModularMech.Tests
{
    /// <summary>
    /// <see cref="IPartData"/> のテスト用 POCO 実装。ScriptableObject を EditMode で
    /// 生成しないための代替(mech-tester の方針)。全プロパティを可変にして
    /// テストごとに自由に組み立てられるようにしてある。
    /// </summary>
    public sealed class FakePartData : IPartData
    {
        public string PartId { get; set; }
        public PartSlot Slot { get; set; }
        public PartStats Stats { get; set; }
        public CapabilityFlags GrantedCapabilities { get; set; }

        /// <summary>Legs スロット以外は null のまま使う。</summary>
        public ILocomotionProfileData LocomotionProfileData { get; set; }
    }
}
