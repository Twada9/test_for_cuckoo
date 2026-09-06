using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;

namespace ModularMech.Tests
{
    /// <summary>
    /// テスト全体で使い回す共通ビルダ。各テストファイルがバラバラに
    /// フェイクを組み立てないよう、ここに1箇所へ集約する(mech-tester の方針)。
    /// 値はすべて呼び出し側が明示できるようにし、暗黙のデフォルトに依存した
    /// テストにならないよう最小限のデフォルト値のみ与える。
    /// </summary>
    public static class PartDataBuilder
    {
        public static FakeLocomotionProfileData Legs(
            LocomotionType type = LocomotionType.Biped,
            float weightCapacity = 100f,
            float baseMoveSpeed = 5f,
            float baseTurnSpeed = 180f,
            float baseJumpPower = 5f,
            float acceleration = 10f,
            float groundOffset = 0f)
        {
            return new FakeLocomotionProfileData
            {
                Type = type,
                BaseMoveSpeed = baseMoveSpeed,
                BaseTurnSpeed = baseTurnSpeed,
                BaseJumpPower = baseJumpPower,
                Acceleration = acceleration,
                GroundOffset = groundOffset,
                WeightCapacity = weightCapacity,
            };
        }

        public static FakePartData Part(
            string partId,
            PartSlot slot,
            float weight = 0f,
            float powerOutput = 0f,
            float powerDraw = 0f,
            CapabilityFlags capabilities = CapabilityFlags.None,
            ILocomotionProfileData locomotion = null)
        {
            return new FakePartData
            {
                PartId = partId,
                Slot = slot,
                Stats = new PartStats
                {
                    weight = weight,
                    powerOutput = powerOutput,
                    powerDraw = powerDraw,
                },
                GrantedCapabilities = capabilities,
                LocomotionProfileData = locomotion,
            };
        }

        /// <summary>典型的な二脚 Legs(Walk/Run/Jump 付与)。重量は呼び出し側が過積載率を作るために指定する。</summary>
        public static FakePartData BipedLegs(
            string partId = "legs_biped_01",
            float weight = 20f,
            float weightCapacity = 100f)
        {
            return Part(
                partId,
                PartSlot.Legs,
                weight: weight,
                capabilities: CapabilityFlags.Walk | CapabilityFlags.Run | CapabilityFlags.Jump,
                locomotion: Legs(LocomotionType.Biped, weightCapacity: weightCapacity));
        }

        public static FakePartData Torso(
            string partId = "torso_light_01",
            float weight = 30f,
            float powerOutput = 100f,
            float powerDraw = 0f)
        {
            return Part(partId, PartSlot.Torso, weight: weight, powerOutput: powerOutput, powerDraw: powerDraw);
        }

        public static FakePartData Head(string partId = "head_sensor_01", float weight = 5f, float powerDraw = 0f)
        {
            return Part(partId, PartSlot.Head, weight: weight, powerDraw: powerDraw);
        }

        public static FakePartData ArmLeft(string partId = "arm_standard_l", float weight = 10f, float powerDraw = 0f)
        {
            return Part(partId, PartSlot.ArmLeft, weight: weight, powerDraw: powerDraw);
        }

        public static FakePartData ArmRight(string partId = "arm_standard_r", float weight = 10f, float powerDraw = 0f)
        {
            return Part(partId, PartSlot.ArmRight, weight: weight, powerDraw: powerDraw);
        }

        public static FakePartData HandLeft(string partId = "hand_tool_l", float weight = 2f)
        {
            return Part(partId, PartSlot.HandLeft, weight: weight);
        }

        public static FakePartCatalog Catalog(params IPartData[] parts)
        {
            var catalog = new FakePartCatalog();
            foreach (var part in parts)
            {
                catalog.Add(part);
            }
            return catalog;
        }

        /// <summary>
        /// <see cref="LoadoutValidator"/> の防御的チェック(通常は Loadout.TryEquip が弾くため
        /// 到達しないパス)を直接叩くための、ResolvedLoadout の素組み立て。
        /// スロットキーと中身の Slot が食い違う不正な組み合わせも作れる。
        /// </summary>
        public static ResolvedLoadout Resolved(
            IReadOnlyDictionary<PartSlot, IPartData> parts = null,
            IReadOnlyDictionary<PartSlot, string> missing = null)
        {
            var partsDict = new Dictionary<PartSlot, IPartData>();
            if (parts != null)
            {
                foreach (var kv in parts)
                {
                    partsDict[kv.Key] = kv.Value;
                }
            }

            var missingDict = new Dictionary<PartSlot, string>();
            if (missing != null)
            {
                foreach (var kv in missing)
                {
                    missingDict[kv.Key] = kv.Value;
                }
            }

            return new ResolvedLoadout(partsDict, missingDict);
        }
    }
}
