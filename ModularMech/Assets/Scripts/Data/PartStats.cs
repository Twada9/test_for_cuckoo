using System;

namespace ModularMech.Data
{
    /// <summary>
    /// パーツ1個分のステータス寄与。v1 は加算補正のみで、乗算補正は導入しない
    /// (混在させると打ち消し合いで破綻するため。設計ドキュメント §10)。
    /// </summary>
    [Serializable]
    public struct PartStats
    {
        public float weight;          // 総重量に加算
        public float powerOutput;     // Torso が供給、他パーツが消費
        public float powerDraw;
        public float moveSpeedMod;    // 加算補正
        public float turnSpeedMod;
        public float jumpPowerMod;
        public float stability;       // 高いほど加減速が緩やか

        public static readonly PartStats Zero = default;

        public static PartStats operator +(PartStats a, PartStats b)
        {
            return new PartStats
            {
                weight       = a.weight + b.weight,
                powerOutput  = a.powerOutput + b.powerOutput,
                powerDraw    = a.powerDraw + b.powerDraw,
                moveSpeedMod = a.moveSpeedMod + b.moveSpeedMod,
                turnSpeedMod = a.turnSpeedMod + b.turnSpeedMod,
                jumpPowerMod = a.jumpPowerMod + b.jumpPowerMod,
                stability    = a.stability + b.stability,
            };
        }

        public override string ToString()
        {
            return $"w={weight:0.##} out={powerOutput:0.##} draw={powerDraw:0.##} " +
                   $"spd+{moveSpeedMod:0.##} turn+{turnSpeedMod:0.##} jump+{jumpPowerMod:0.##} stab={stability:0.##}";
        }
    }
}
