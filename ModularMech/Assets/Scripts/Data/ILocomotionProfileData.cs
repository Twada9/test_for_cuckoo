namespace ModularMech.Data
{
    /// <summary>移動方式の数値定義の読み取り面。<see cref="LocomotionProfile"/> が実装する。</summary>
    public interface ILocomotionProfileData
    {
        LocomotionType Type { get; }
        float BaseMoveSpeed { get; }
        float BaseTurnSpeed { get; }
        float BaseJumpPower { get; }
        float Acceleration { get; }

        /// <summary>接地高さ。ホバー脚はここが正の値になり、常時浮く。</summary>
        float GroundOffset { get; }

        /// <summary>これを超えると過積載。0 以下は「容量未設定」として過積載判定を行わない。</summary>
        float WeightCapacity { get; }
    }
}
