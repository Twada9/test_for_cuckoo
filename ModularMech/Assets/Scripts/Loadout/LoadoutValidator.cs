using System.Collections.Generic;
using ModularMech.Data;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// 構成の検証(設計ドキュメント §3.2)。UnityEngine には依存しないので EditMode テストから直接叩ける。
    ///
    /// 重さの割り当て方針:
    /// - 構成として成立しないもの(必須スロット欠損 / スロット不一致 / 腕なしの手持ち / 未知 ID)は Error。
    /// - 極端な構成を試す余地を残すため、過積載とパワー不足は Warning にして装備自体は許す。
    /// </summary>
    public static class LoadoutValidator
    {
        public static LoadoutValidation Validate(Loadout loadout, IPartCatalog catalog)
        {
            return Validate(LoadoutResolver.Resolve(loadout, catalog));
        }

        public static LoadoutValidation Validate(ResolvedLoadout resolved)
        {
            if (resolved == null)
            {
                return new LoadoutValidation(new List<ValidationIssue>
                {
                    ValidationIssue.Error(ValidationCode.MissingTorso, "構成が読み込めませんでした。Torso と Legs を装備してください。"),
                });
            }

            var issues = new List<ValidationIssue>();

            ValidateRequiredSlots(resolved, issues);
            ValidateUnknownPartIds(resolved, issues);
            ValidateSlotMatchAndHands(resolved, issues);
            ValidatePowerBudget(resolved, issues);
            ValidateWeightCapacity(resolved, issues);

            return issues.Count == 0 ? LoadoutValidation.Valid : new LoadoutValidation(issues);
        }

        // RequiredSlots: Torso / Legs が無い構成は出撃不可(設計ドキュメント §2.1)。
        private static void ValidateRequiredSlots(ResolvedLoadout resolved, List<ValidationIssue> issues)
        {
            if (resolved.Torso == null)
            {
                issues.Add(ValidationIssue.Error(ValidationCode.MissingTorso, PartSlot.Torso,
                    "Torso が装備されていません。胴体は他のパーツの取り付け基準になるため必須です。"));
            }

            if (resolved.Legs == null)
            {
                issues.Add(ValidationIssue.Error(ValidationCode.MissingLegs, PartSlot.Legs,
                    "Legs が装備されていません。脚が移動方式を決めるため必須です。"));
            }
        }

        // UnknownPartId: カタログに無い ID。
        //
        // 【判断】これを Warning ではなく Error にする。
        // 復元処理(§7)は欠損 ID のスロットを空にして先へ進むので、機体は「壊れずに」出来上がる。
        // しかしその機体はプレイヤーが保存したものとは別物であり、黙って出撃させると
        // 「保存したはずのパーツが無い」ことに気づけない。Error にして IsDeployable=false とし、
        // ガレージで組み直すことを強制する方が、データ欠損の発覚が早く安全側に倒れる。
        // (欠損しても例外を投げず状態を保持する、という §7 の原則自体は Resolver 側で守っている。)
        private static void ValidateUnknownPartIds(ResolvedLoadout resolved, List<ValidationIssue> issues)
        {
            if (resolved.MissingBySlot.Count == 0) return;

            for (int i = 0; i < PartSlots.All.Length; i++)
            {
                var slot = PartSlots.All[i];
                if (!resolved.MissingBySlot.TryGetValue(slot, out var partId)) continue;

                issues.Add(ValidationIssue.Error(ValidationCode.UnknownPartId, slot,
                    $"{slot} のパーツ ID「{partId}」がカタログに見つかりません。このスロットは空として扱われます。"));
            }
        }

        // SlotMatch / HandRequiresArm。
        // Loadout.TryEquip が既に弾いているので通常は発生しないが、
        // セーブデータやエディタ生成など Loadout を経由しない構成もあり得るため防御的に検証する。
        private static void ValidateSlotMatchAndHands(ResolvedLoadout resolved, List<ValidationIssue> issues)
        {
            for (int i = 0; i < PartSlots.All.Length; i++)
            {
                var slot = PartSlots.All[i];
                if (!resolved.TryGet(slot, out var part) || part == null) continue;

                if (part.Slot != slot)
                {
                    issues.Add(ValidationIssue.Error(ValidationCode.SlotMismatch, slot,
                        $"パーツ「{part.PartId}」は {part.Slot} 用のため、{slot} には装備できません。"));
                }

                if (PartSlots.TryGetRequiredArm(slot, out var armSlot) &&
                    (!resolved.TryGet(armSlot, out var arm) || arm == null))
                {
                    issues.Add(ValidationIssue.Error(ValidationCode.HandWithoutArm, slot,
                        $"{slot} に「{part.PartId}」が装備されていますが、前提となる {armSlot} がありません。"));
                }
            }
        }

        // PowerBudget: Σ powerDraw > Σ powerOutput → 警告。装備は許すが反応速度が落ちる(§3.3)。
        private static void ValidatePowerBudget(ResolvedLoadout resolved, List<ValidationIssue> issues)
        {
            float output = 0f;
            float draw = 0f;
            foreach (var part in resolved.All)
            {
                if (part == null) continue;
                var stats = part.Stats;
                output += stats.powerOutput;
                draw += stats.powerDraw;
            }

            if (draw <= output) return;

            var shortage = draw - output;

            // powerRatio は StatAggregator と同じ定義。draw > output なので draw > 0 は保証される
            // (output が負になる想定は無いが、負でも下の除算は成立する)。
            var ratioPercent = draw > 0f ? output / draw * 100f : 0f;

            issues.Add(ValidationIssue.Warning(ValidationCode.PowerBudgetExceeded,
                $"電力が {shortage:0.##} 不足しています(消費 {draw:0.##} / 出力 {output:0.##})。" +
                $"加速と旋回速度が約 {ratioPercent:0}% に低下します。"));
        }

        // WeightCapacity: Σ weight > legs.WeightCapacity → 警告。過積載ペナルティ付きで許可(§3.3)。
        private static void ValidateWeightCapacity(ResolvedLoadout resolved, List<ValidationIssue> issues)
        {
            var legs = resolved.Legs;
            if (legs == null) return;

            var profile = legs.LocomotionProfileData;
            if (profile == null) return;

            var capacity = profile.WeightCapacity;

            // 容量未設定(0 以下)は判定しない。StatAggregator のゼロ除算の既定と揃える。
            if (capacity <= 0f) return;

            float totalWeight = 0f;
            foreach (var part in resolved.All)
            {
                if (part == null) continue;
                totalWeight += part.Stats.weight;
            }

            if (totalWeight <= capacity) return;

            var excess = totalWeight - capacity;
            var ratio = totalWeight / capacity;

            // 速度低下の具体値はここで再計算しない(ペナルティ式は StatAggregator の一箇所に置く)。
            var message =
                $"重量が上限を {excess:0.##} 超過しています(総重量 {totalWeight:0.##} / 上限 {capacity:0.##}、積載率 {ratio * 100f:0}%)。" +
                "移動速度が低下し、走行できなくなります。";

            if (ratio > 1.5f)
            {
                message += "さらに積載率 150% を超えているため、ジャンプもできません。";
            }

            issues.Add(ValidationIssue.Warning(ValidationCode.WeightCapacityExceeded, PartSlot.Legs, message));
        }
    }
}
