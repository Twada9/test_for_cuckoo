using ModularMech.Data;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 構成1つ分の集約結果(生の合計値 + ペナルティ適用後の倍率と能力)。
    /// 移動コントローラはここと <see cref="ILocomotionProfileData"/> だけを見て動く。
    /// </summary>
    public readonly struct StatBlock
    {
        public StatBlock(
            PartStats raw,
            float weightCapacity,
            float overweightRatio,
            float powerRatio,
            float speedMultiplier,
            float accelerationMultiplier,
            float turnSpeedMultiplier,
            CapabilityFlags capabilities)
        {
            Raw = raw;
            WeightCapacity = weightCapacity;
            OverweightRatio = overweightRatio;
            PowerRatio = powerRatio;
            SpeedMultiplier = speedMultiplier;
            AccelerationMultiplier = accelerationMultiplier;
            TurnSpeedMultiplier = turnSpeedMultiplier;
            Capabilities = capabilities;
        }

        /// <summary>パーツの加算補正の合計。v1 は加算のみで、乗算は下の各 Multiplier に集約されている。</summary>
        public PartStats Raw { get; }

        public float TotalWeight => Raw.weight;
        public float TotalPowerOutput => Raw.powerOutput;
        public float TotalPowerDraw => Raw.powerDraw;

        /// <summary>脚の積載上限。脚が無い / 未設定なら 0。</summary>
        public float WeightCapacity { get; }

        /// <summary>総重量 / 積載上限。上限が 0 以下(判定不能)のときは 0。</summary>
        public float OverweightRatio { get; }

        /// <summary>出力 / 消費。消費が 0 以下(判定不要)のときは 1。</summary>
        public float PowerRatio { get; }

        public float SpeedMultiplier { get; }
        public float AccelerationMultiplier { get; }
        public float TurnSpeedMultiplier { get; }

        /// <summary>ペナルティ適用後の能力。過積載時は Run が、極端な過積載では Jump も落ちている。</summary>
        public CapabilityFlags Capabilities { get; }

        public bool IsOverweight => OverweightRatio > 1f;
        public bool IsUnderpowered => PowerRatio < 1f;

        /// <summary>パーツが1つも無い状態。倍率は 1 で、default(StatBlock) とは違うので必ずこちらを使う。</summary>
        public static StatBlock Empty =>
            new StatBlock(PartStats.Zero, 0f, 0f, 1f, 1f, 1f, 1f, CapabilityFlags.None);

        public override string ToString()
        {
            return $"weight {TotalWeight:0.##}/{WeightCapacity:0.##} (x{OverweightRatio:0.##}) " +
                   $"power {TotalPowerDraw:0.##}/{TotalPowerOutput:0.##} (x{PowerRatio:0.##}) " +
                   $"spd x{SpeedMultiplier:0.##} accel x{AccelerationMultiplier:0.##} turn x{TurnSpeedMultiplier:0.##} " +
                   $"caps [{Capabilities}]";
        }
    }
}
