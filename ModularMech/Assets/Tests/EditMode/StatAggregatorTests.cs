using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using ModularMech.Mechs;
using NUnit.Framework;

namespace ModularMech.Tests
{
    /// <summary>
    /// StatAggregator.Aggregate のペナルティ式(設計ドキュメント §3.3、ModularMech/CLAUDE.md
    /// の「ゼロ除算の扱い」節)を固定する。期待値はすべて式からの手計算(コメント参照)であり、
    /// 実装のコードから逆算したものではない。
    ///
    /// D-8(CLAUDE.md 追加確定事項)の裁定:「走行不可」= Run 剥奪であり、移動そのものは
    /// 可能(Walk は剥奪されない)。過積載のどのテストでも Walk が残ることを必ず確認する。
    /// </summary>
    [TestFixture]
    public sealed class StatAggregatorTests
    {
        private const float Tol = 1e-4f;

        // --- 過積載: 速度倍率と能力剥奪の境界値 -------------------------------------
        //
        // 式: overweightRatio = totalWeight / capacity
        //     if (ratio > 1) speedMul *= Lerp(1, 0.4, (ratio-1)/0.5); caps &= ~Run;
        //                     if (ratio > 1.5) caps &= ~Jump;
        // capacity = 100 に固定し、weight = ratio*100 で狙った積載率を作る。
        // ここでは脚以外を一切装備しないので「総重量 = 脚の重量」になる(他パーツを足す場合は
        // その重量も総重量に入るので、狙った比を作るには全パーツの重量を明示すること)。
        //
        // ratio=1.0  (weight=100): 1.0 は "超" ではないためペナルティ無し。
        // ratio=1.1  (weight=110): t=(1.1-1)/0.5=0.2  -> Lerp(1,0.4,0.2)=1+(0.4-1)*0.2=0.88
        // ratio=1.25 (weight=125): t=(1.25-1)/0.5=0.5 -> Lerp(1,0.4,0.5)=1+(0.4-1)*0.5=0.7
        // ratio=1.5  (weight=150): t=(1.5-1)/0.5=1.0  -> Lerp(1,0.4,1.0)=0.4。
        //            ratio>1.5 は偽(ちょうど1.5)なので Jump はまだ生きている。
        // ratio=1.6  (weight=160): t_raw=(1.6-1)/0.5=1.2 -> Mathf.Lerp は t をクランプするので 1.0 扱い
        //            -> 0.4。ratio>1.5 が真なので Jump も剥奪。
        // ratio=2.0  (weight=200): t_raw=2.0 -> クランプで 1.0 -> 0.4(下げ止まり)。Jump も剥奪。
        [TestCase(100f, 1f, false, false, TestName = "Overweight_RatioExactlyOne_NoPenalty")]
        [TestCase(110f, 0.88f, true, false, TestName = "Overweight_RatioOverOne_SpeedDownRunRemoved")]
        [TestCase(125f, 0.7f, true, false, TestName = "Overweight_Ratio125_SpeedDownRunRemoved")]
        [TestCase(150f, 0.4f, true, false, TestName = "Overweight_RatioExactlyOnePointFive_JumpStillAlive")]
        [TestCase(160f, 0.4f, true, true, TestName = "Overweight_RatioOverOnePointFive_JumpRemoved")]
        [TestCase(200f, 0.4f, true, true, TestName = "Overweight_RatioTwo_SpeedFloorsAtPointFour")]
        public void Aggregate_OverweightRatioBoundaries_MatchDesignFormula(
            float weight, float expectedSpeedMultiplier, bool expectRunRemoved, bool expectJumpRemoved)
        {
            var legs = PartDataBuilder.BipedLegs(weight: weight, weightCapacity: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Legs] = legs,
            });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.OverweightRatio, Is.EqualTo(weight / 100f).Within(Tol));
            Assert.That(block.SpeedMultiplier, Is.EqualTo(expectedSpeedMultiplier).Within(Tol));
            Assert.That(block.Capabilities.Has(CapabilityFlags.Run), Is.EqualTo(!expectRunRemoved),
                "Run の剥奪判定が式と一致しない");
            Assert.That(block.Capabilities.Has(CapabilityFlags.Jump), Is.EqualTo(!expectJumpRemoved),
                "Jump の剥奪判定が式と一致しない(1.5 は超ではなく、1.5 を超えて初めて剥奪される)");

            // D-8: 過積載は「走行不可」(Run 剥奪)であって「移動不能」ではない。Walk は常に残る。
            Assert.That(block.Capabilities.Has(CapabilityFlags.Walk), Is.True,
                "D-8: 過積載でも Walk は剥奪されない(移動そのものは可能)");
        }

        [Test]
        public void IsOverweight_RatioExactlyOne_IsFalse()
        {
            var legs = PartDataBuilder.BipedLegs(weight: 100f, weightCapacity: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData> { [PartSlot.Legs] = legs });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.IsOverweight, Is.False, "ちょうど 1.0 は超過ではない(> の境界)");
        }

        [Test]
        public void IsOverweight_RatioAboveOne_IsTrue()
        {
            var legs = PartDataBuilder.BipedLegs(weight: 101f, weightCapacity: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData> { [PartSlot.Legs] = legs });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.IsOverweight, Is.True);
        }

        [Test]
        public void Aggregate_WeightCapacityZeroOrLess_OverweightJudgmentSkippedEntirely()
        {
            // 脚は装備されているが WeightCapacity <= 0(容量未設定)。
            // どれだけ重くても過積載判定そのものを行わない(CLAUDE.md「ゼロ除算の扱い」)。
            var legs = PartDataBuilder.BipedLegs(weight: 10000f, weightCapacity: 0f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData> { [PartSlot.Legs] = legs });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.WeightCapacity, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.OverweightRatio, Is.EqualTo(0f).Within(Tol), "容量未設定時は比を計算しない(0固定)");
            Assert.That(block.SpeedMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.Capabilities.Has(CapabilityFlags.Run), Is.True, "判定不能なので Run は剥奪されない");
            Assert.That(block.Capabilities.Has(CapabilityFlags.Jump), Is.True, "判定不能なので Jump は剥奪されない");
        }

        [Test]
        public void Aggregate_NegativeWeightCapacity_TreatedSameAsZero()
        {
            var legs = PartDataBuilder.BipedLegs(weight: 500f, weightCapacity: -10f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData> { [PartSlot.Legs] = legs });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.OverweightRatio, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.Capabilities.Has(CapabilityFlags.Run), Is.True);
        }

        [Test]
        public void Aggregate_LegsNotEquipped_OverweightJudgmentSkipped()
        {
            // 脚スロットそのものが無い(resolved.Legs == null)。容量0のケースとは
            // コードパスが異なる(legs == null で分岐)ため、別テストとして固定する。
            var torso = PartDataBuilder.Part(
                "torso_x", PartSlot.Torso, weight: 999f, capabilities: CapabilityFlags.Run);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData> { [PartSlot.Torso] = torso });

            Assert.That(resolved.Legs, Is.Null);

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.WeightCapacity, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.OverweightRatio, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.SpeedMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.Capabilities.Has(CapabilityFlags.Run), Is.True,
                "脚が無ければ過積載判定自体が走らないので、他パーツが持つ Run は剥奪されない");
        }

        [Test]
        public void Aggregate_ResolvedIsNull_ReturnsSameDefaultsAsEmpty()
        {
            var block = StatAggregator.Aggregate(null);

            Assert.That(block.TotalWeight, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.WeightCapacity, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.OverweightRatio, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.PowerRatio, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.SpeedMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.AccelerationMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.TurnSpeedMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.Capabilities, Is.EqualTo(CapabilityFlags.None));
        }

        // --- パワー不足: 加速・旋回倍率 ----------------------------------------------
        //
        // 式: powerRatio = totalOutput / totalDraw; if (ratio < 1) accelMul *= ratio; turnMul *= ratio;
        [Test]
        public void PowerRatio_ExactlyOne_NoPenalty()
        {
            var torso = PartDataBuilder.Torso(powerOutput: 100f, powerDraw: 0f);
            var arm = PartDataBuilder.ArmLeft(powerDraw: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.ArmLeft] = arm,
            });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.PowerRatio, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.AccelerationMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.TurnSpeedMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.IsUnderpowered, Is.False, "ちょうど 1.0 は不足ではない(< の境界)");
        }

        [Test]
        public void PowerRatio_LessThanOne_ReducesAccelerationAndTurnByExactRatio()
        {
            // output=50 / draw=100 -> ratio=0.5
            var torso = PartDataBuilder.Torso(powerOutput: 50f, powerDraw: 0f);
            var arm = PartDataBuilder.ArmLeft(powerDraw: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.ArmLeft] = arm,
            });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.PowerRatio, Is.EqualTo(0.5f).Within(Tol));
            Assert.That(block.AccelerationMultiplier, Is.EqualTo(0.5f).Within(Tol));
            Assert.That(block.TurnSpeedMultiplier, Is.EqualTo(0.5f).Within(Tol));
            Assert.That(block.IsUnderpowered, Is.True);
        }

        [Test]
        public void PowerDrawZeroOrLess_RatioForcedToOne_NoPenaltyRegardlessOfOutput()
        {
            // draw <= 0 は比を取る意味が無いため 1(ペナルティ無し)固定。
            // output も 0 の極端なケースを含めて確認する。
            var torso = PartDataBuilder.Torso(powerOutput: 0f, powerDraw: 0f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData> { [PartSlot.Torso] = torso });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.TotalPowerDraw, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.PowerRatio, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.AccelerationMultiplier, Is.EqualTo(1f).Within(Tol));
            Assert.That(block.TurnSpeedMultiplier, Is.EqualTo(1f).Within(Tol));
        }

        [Test]
        public void PowerOutputZero_DrawPositive_RatioZero_AccelAndTurnFullyZeroed()
        {
            // output=0 / draw=50 -> ratio=0。0 < 1 なので通常のペナルティ経路に乗り、
            // accelMul/turnMul は 0 まで落ちる(消費に対して供給が皆無という極端な構成)。
            var torso = PartDataBuilder.Torso(powerOutput: 0f, powerDraw: 0f);
            var arm = PartDataBuilder.ArmLeft(powerDraw: 50f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.ArmLeft] = arm,
            });

            var block = StatAggregator.Aggregate(resolved);

            Assert.That(block.TotalPowerOutput, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.PowerRatio, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.AccelerationMultiplier, Is.EqualTo(0f).Within(Tol));
            Assert.That(block.TurnSpeedMultiplier, Is.EqualTo(0f).Within(Tol));
        }

        [Test]
        public void Aggregate_CombinesOverweightAndPowerPenaltiesIndependently()
        {
            // 過積載とパワー不足は別の乗数(SpeedMultiplier と Accel/TurnMultiplier)に
            // 分かれているため、両方が同時に発生しても互いを打ち消さないことを確認する。
            //
            // 総重量は「機体に載っている全パーツの合計」であって脚の重量ではない。
            // 脚 85 + 胴 30 + 腕 10 = 125、上限 100 -> ratio=1.25 -> speedMul=0.7(上と同じ手計算)。
            // 重量は3パーツすべてに明示指定する。ビルダの既定重量(Torso=30 / ArmLeft=10)に
            // 依存させると、既定値を動かした瞬間に狙った積載率が静かにずれる
            // (PartDataBuilder の「暗黙のデフォルトに依存したテストにしない」方針)。
            //
            // output=50/draw=100 -> powerRatio=0.5 -> accelMul=turnMul=0.5
            var legs = PartDataBuilder.BipedLegs(weight: 85f, weightCapacity: 100f);
            var torso = PartDataBuilder.Torso(weight: 30f, powerOutput: 50f, powerDraw: 0f);
            var arm = PartDataBuilder.ArmLeft(weight: 10f, powerDraw: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Legs] = legs,
                [PartSlot.Torso] = torso,
                [PartSlot.ArmLeft] = arm,
            });

            var block = StatAggregator.Aggregate(resolved);

            // 積載率の前提そのものを先に固定する。ここがずれると SpeedMultiplier の期待値も
            // 意味を失うため、失敗時にどちらが崩れたのかが分かるようにしておく。
            Assert.That(block.TotalWeight, Is.EqualTo(125f).Within(Tol), "3パーツの合計重量");
            Assert.That(block.OverweightRatio, Is.EqualTo(1.25f).Within(Tol));

            Assert.That(block.SpeedMultiplier, Is.EqualTo(0.7f).Within(Tol));
            Assert.That(block.AccelerationMultiplier, Is.EqualTo(0.5f).Within(Tol));
            Assert.That(block.TurnSpeedMultiplier, Is.EqualTo(0.5f).Within(Tol));
            Assert.That(block.Capabilities.Has(CapabilityFlags.Run), Is.False);
        }
    }
}
