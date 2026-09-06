using ModularMech.Data;
using ModularMech.Loadouts;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 解決済み構成からステータスと能力を集約し、ペナルティを適用する(設計ドキュメント §3.3)。
    /// 式は設計ドキュメントのまま変えない。ペナルティの乗算はここだけに置き、
    /// パーツ側の補正は加算に統一しておく(混在させると打ち消し合いで破綻するため)。
    ///
    /// <para>
    /// 純粋ロジックなので <c>UnityEngine</c> に依存しない(CLAUDE.md 原則9)。
    /// EditMode テストから ScriptableObject を用意せずに直接叩けることが条件になっている。
    /// </para>
    /// </summary>
    public static class StatAggregator
    {
        // 過積載時の速度倍率の下限と、そこへ到達する超過率の幅(設計ドキュメント §3.3)。
        private const float OverweightSpeedFloor = 0.4f;
        private const float OverweightRampRange = 0.5f;

        /// <summary>
        /// 集約する。<paramref name="resolved"/> が null / 空でも例外を投げず、倍率 1 の素の値を返す。
        /// 装備変更時にだけ呼ぶこと(毎フレーム呼ぶ想定ではない。辞書の列挙で列挙子の boxing が起きる)。
        /// </summary>
        public static StatBlock Aggregate(ResolvedLoadout resolved)
        {
            var raw = PartStats.Zero;
            var capabilities = CapabilityFlags.None;

            if (resolved != null)
            {
                // LINQ を使わないのは、集約が Update から呼ばれても割り当てを増やさないようにするため。
                foreach (var part in resolved.All)
                {
                    if (part == null) continue;
                    raw += part.Stats;
                    capabilities |= part.GrantedCapabilities;
                }
            }

            var speedMultiplier = 1f;
            var accelerationMultiplier = 1f;
            var turnSpeedMultiplier = 1f;

            // --- 過積載 ---------------------------------------------------------------
            var weightCapacity = 0f;
            var legs = resolved?.Legs;
            if (legs != null)
            {
                var profile = legs.LocomotionProfileData;
                if (profile != null) weightCapacity = profile.WeightCapacity;
            }

            // 脚が無い / 容量未設定(0 以下)なら過積載判定そのものを行わない。
            var overweightRatio = 0f;
            if (weightCapacity > 0f)
            {
                overweightRatio = raw.weight / weightCapacity;
                if (overweightRatio > 1f)
                {
                    // Lerp は t を 0..1 にクランプするため、超過率 1.5 以上では 0.4 で下げ止まる。
                    speedMultiplier *= Lerp(1f, OverweightSpeedFloor,
                        (overweightRatio - 1f) / OverweightRampRange);

                    capabilities &= ~CapabilityFlags.Run;
                    if (overweightRatio > 1.5f)
                    {
                        capabilities &= ~CapabilityFlags.Jump;
                    }
                }
            }

            // --- パワー不足 -----------------------------------------------------------
            // 消費が 0 以下なら比を取る意味が無いので 1(ペナルティ無し)とする。
            var powerRatio = 1f;
            if (raw.powerDraw > 0f)
            {
                powerRatio = raw.powerOutput / raw.powerDraw;
                if (powerRatio < 1f)
                {
                    accelerationMultiplier *= powerRatio;
                    turnSpeedMultiplier *= powerRatio;
                }
            }

            return new StatBlock(
                raw,
                weightCapacity,
                overweightRatio,
                powerRatio,
                speedMultiplier,
                accelerationMultiplier,
                turnSpeedMultiplier,
                capabilities);
        }

        /// <summary>
        /// <c>UnityEngine.Mathf.Lerp</c> と同じ挙動の線形補間(t を 0..1 にクランプする)。
        /// 式を変えないために自前で持つ ―― §3.3 が想定しているのはクランプ付きの Lerp であり、
        /// クランプが無いと超過率 1.5 を超えたところで速度倍率が 0.4 を割り込み、
        /// やがて負になって機体が後退する。
        /// クランプ判定を <c>&lt;</c> / <c>&gt;</c> で書くのは Mathf.Clamp01 と同じ形にするためで、
        /// NaN が来たときの結果(そのまま NaN)まで一致する。
        /// </summary>
        private static float Lerp(float a, float b, float t)
        {
            if (t < 0f)
            {
                t = 0f;
            }
            else if (t > 1f)
            {
                t = 1f;
            }

            return a + (b - a) * t;
        }
    }
}
