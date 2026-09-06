using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using NUnit.Framework;

namespace ModularMech.Tests
{
    /// <summary>
    /// LoadoutValidator を設計ドキュメント §3.2 の表に沿って1行ずつ固定する。
    ///
    /// 設計判断(§3.2 / CLAUDE.md 設計原則4): 拒否するのは SlotMismatch と
    /// HandWithoutArm(実装名は HandWithoutArm)のみ。PowerBudget と WeightCapacity は
    /// 警告であって拒否ではない(IsDeployable を落とさない)。
    ///
    /// 実装メモ: 実装は UnknownPartId を Error として扱う(ValidateUnknownPartIds の
    /// コメント参照)。設計ドキュメント §3.2 の表自体には UnknownPartId の行が無く、
    /// これは実装側の追加判断であり「拒否」とは意味が違う(装備自体は §7 の規則どおり
    /// 空スロットとして許可されるが、出撃だけを止める)。テストはこの実装の挙動を
    /// そのまま固定し、報告でその判断を明示する。
    /// </summary>
    [TestFixture]
    public sealed class LoadoutValidatorTests
    {
        [Test]
        public void Validate_MissingTorsoAndLegs_BothAreErrors_NotDeployable()
        {
            var resolved = PartDataBuilder.Resolved();

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(result.IsDeployable, Is.False);
            Assert.That(HasError(result, ValidationCode.MissingTorso), Is.True);
            Assert.That(HasError(result, ValidationCode.MissingLegs), Is.True);
        }

        [Test]
        public void Validate_TorsoAndLegsPresent_NoRequiredSlotErrors()
        {
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = PartDataBuilder.Torso(),
                [PartSlot.Legs] = PartDataBuilder.BipedLegs(weight: 10f, weightCapacity: 100f),
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasError(result, ValidationCode.MissingTorso), Is.False);
            Assert.That(HasError(result, ValidationCode.MissingLegs), Is.False);
        }

        [Test]
        public void Validate_UnknownPartId_ThroughLoadoutAndCatalog_IsErrorAndNotDeployable()
        {
            // Loadout 経由(TryEquip)は ID の実在を確認しないため、カタログに無い ID を
            // 装備した「体」を作れる。ここでは実際の Loadout + カタログ差し替えの経路で確認する。
            var torso = PartDataBuilder.Torso();
            var legs = PartDataBuilder.BipedLegs();

            var loadout = new Loadout("test");
            Assert.That(loadout.TryEquip(PartSlot.Torso, torso, out _), Is.True);
            Assert.That(loadout.TryEquip(PartSlot.Legs, legs, out _), Is.True);

            // カタログが差し替わり、Legs の ID が消えたことを模擬する。
            var catalogAfterPatch = PartDataBuilder.Catalog(torso);

            var result = loadout.Validate(catalogAfterPatch);

            Assert.That(HasError(result, ValidationCode.UnknownPartId), Is.True);
            Assert.That(result.IsDeployable, Is.False,
                "実装は UnknownPartId を Error として扱う(出撃前に組み直しを強制する設計判断)");
        }

        [Test]
        public void Validate_SlotMismatch_DirectlyConstructedResolvedLoadout_IsErrorAndNotDeployable()
        {
            // Loadout.TryEquip は SlotMismatch を必ず拒否するため、この不正状態は
            // 通常の経路では作れない。バリデータの防御的チェック(コード内コメント参照)を
            // 直接叩くため、ResolvedLoadout を素で組み立てる。
            var mismatched = PartDataBuilder.Part("arm_l", PartSlot.ArmLeft, weight: 1f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = PartDataBuilder.Torso(),
                [PartSlot.Legs] = PartDataBuilder.BipedLegs(),
                // Head スロットのキーに ArmLeft 用パーツを押し込む(食い違い)。
                [PartSlot.Head] = mismatched,
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasError(result, ValidationCode.SlotMismatch), Is.True);
            Assert.That(result.IsDeployable, Is.False);
        }

        [Test]
        public void Validate_HandWithoutArm_DirectlyConstructedResolvedLoadout_IsErrorAndNotDeployable()
        {
            // 同様に TryEquip では作れない状態(手はあるが腕が無い)を直接組み立てて確認する。
            var hand = PartDataBuilder.HandLeft();
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = PartDataBuilder.Torso(),
                [PartSlot.Legs] = PartDataBuilder.BipedLegs(),
                [PartSlot.HandLeft] = hand,
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasError(result, ValidationCode.HandWithoutArm), Is.True);
            Assert.That(result.IsDeployable, Is.False);
        }

        [Test]
        public void Validate_HandWithArmPresent_NoHandWithoutArmError()
        {
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = PartDataBuilder.Torso(),
                [PartSlot.Legs] = PartDataBuilder.BipedLegs(),
                [PartSlot.ArmLeft] = PartDataBuilder.ArmLeft(),
                [PartSlot.HandLeft] = PartDataBuilder.HandLeft(),
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasError(result, ValidationCode.HandWithoutArm), Is.False);
        }

        [Test]
        public void Validate_PowerBudgetExceeded_IsWarningOnly_StillDeployable()
        {
            // draw(100) > output(50) -> 警告。設計ドキュメント §3.2: 「装備は可能だが性能低下」。
            var torso = PartDataBuilder.Torso(powerOutput: 50f, powerDraw: 0f);
            var legs = PartDataBuilder.BipedLegs(weight: 10f, weightCapacity: 100f);
            var arm = PartDataBuilder.ArmLeft(powerDraw: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.Legs] = legs,
                [PartSlot.ArmLeft] = arm,
            });

            var result = LoadoutValidator.Validate(resolved);

            var issue = FindIssue(result, ValidationCode.PowerBudgetExceeded);
            Assert.That(issue.HasValue, Is.True);
            Assert.That(issue.Value.Severity, Is.EqualTo(ValidationSeverity.Warning));
            Assert.That(result.IsDeployable, Is.True, "パワー不足は拒否ではない");
            Assert.That(result.HasWarnings, Is.True);
        }

        [Test]
        public void Validate_PowerBudgetExactlyBalanced_NoWarning()
        {
            // draw == output はちょうど境界。§3.2 は "<=" を許容条件としているので警告無し。
            var torso = PartDataBuilder.Torso(powerOutput: 100f, powerDraw: 0f);
            var legs = PartDataBuilder.BipedLegs(weight: 10f, weightCapacity: 100f);
            var arm = PartDataBuilder.ArmLeft(powerDraw: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.Legs] = legs,
                [PartSlot.ArmLeft] = arm,
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasIssue(result, ValidationCode.PowerBudgetExceeded), Is.False);
        }

        [Test]
        public void Validate_WeightCapacityExceeded_IsWarningOnly_StillDeployable()
        {
            var legs = PartDataBuilder.BipedLegs(weight: 150f, weightCapacity: 100f);
            var torso = PartDataBuilder.Torso();
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.Legs] = legs,
            });

            var result = LoadoutValidator.Validate(resolved);

            var issue = FindIssue(result, ValidationCode.WeightCapacityExceeded);
            Assert.That(issue.HasValue, Is.True);
            Assert.That(issue.Value.Severity, Is.EqualTo(ValidationSeverity.Warning));
            Assert.That(result.IsDeployable, Is.True, "過積載は拒否ではない(設計原則4)");
            Assert.That(result.HasWarnings, Is.True);
        }

        [Test]
        public void Validate_WeightExactlyAtCapacity_NoWarning()
        {
            var legs = PartDataBuilder.BipedLegs(weight: 100f, weightCapacity: 100f);
            var torso = PartDataBuilder.Torso(weight: 0f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.Legs] = legs,
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasIssue(result, ValidationCode.WeightCapacityExceeded), Is.False);
        }

        [Test]
        public void Validate_ValidMinimalLoadout_IsDeployableAndHasNoWarnings()
        {
            var torso = PartDataBuilder.Torso(powerOutput: 100f, powerDraw: 0f);
            var legs = PartDataBuilder.BipedLegs(weight: 10f, weightCapacity: 100f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.Legs] = legs,
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(result.HasIssues, Is.False);
            Assert.That(result.IsDeployable, Is.True);
            Assert.That(result.HasWarnings, Is.False);
        }

        [Test]
        public void Validate_OnlyWarningsPresent_StillDeployableButHasWarnings()
        {
            // 過積載とパワー不足を同時に発生させても、両方とも警告なので出撃は可能。
            var torso = PartDataBuilder.Torso(powerOutput: 10f, powerDraw: 0f);
            var legs = PartDataBuilder.BipedLegs(weight: 200f, weightCapacity: 100f);
            var arm = PartDataBuilder.ArmLeft(powerDraw: 50f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.Legs] = legs,
                [PartSlot.ArmLeft] = arm,
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasIssue(result, ValidationCode.WeightCapacityExceeded), Is.True);
            Assert.That(HasIssue(result, ValidationCode.PowerBudgetExceeded), Is.True);
            Assert.That(result.IsDeployable, Is.True);
            Assert.That(result.HasWarnings, Is.True);
        }

        [Test]
        public void Validate_ErrorAndWarningTogether_NotDeployableButHasWarningsToo()
        {
            // Legs 欠損(Error)と PowerBudget 超過(Warning)を同時に起こし、
            // Error が1件でもあれば全体として IsDeployable=false になることを確認する。
            var torso = PartDataBuilder.Torso(powerOutput: 10f, powerDraw: 0f);
            var arm = PartDataBuilder.ArmLeft(powerDraw: 50f);
            var resolved = PartDataBuilder.Resolved(new Dictionary<PartSlot, IPartData>
            {
                [PartSlot.Torso] = torso,
                [PartSlot.ArmLeft] = arm,
            });

            var result = LoadoutValidator.Validate(resolved);

            Assert.That(HasError(result, ValidationCode.MissingLegs), Is.True);
            Assert.That(HasIssue(result, ValidationCode.PowerBudgetExceeded), Is.True);
            Assert.That(result.IsDeployable, Is.False);
            Assert.That(result.HasWarnings, Is.True);
        }

        [Test]
        public void Validate_NullResolvedLoadout_ReturnsSingleErrorNotDeployable()
        {
            var result = LoadoutValidator.Validate((ResolvedLoadout)null);

            Assert.That(result.IsDeployable, Is.False);
            Assert.That(result.Issues.Count, Is.EqualTo(1));
            Assert.That(result.Issues[0].Severity, Is.EqualTo(ValidationSeverity.Error));
        }

        [Test]
        public void Loadout_Validate_DelegatesToResolverAndValidator()
        {
            // Loadout.Validate(catalog) が Resolver + Validator の合成であることを
            // end-to-end で確認する(個々の単体テストとは別に、委譲そのものを固定する)。
            var torso = PartDataBuilder.Torso(powerOutput: 100f, powerDraw: 0f);
            var legs = PartDataBuilder.BipedLegs(weight: 10f, weightCapacity: 100f);
            var catalog = PartDataBuilder.Catalog(torso, legs);

            var loadout = new Loadout("valid");
            loadout.TryEquip(PartSlot.Torso, torso, out _);
            loadout.TryEquip(PartSlot.Legs, legs, out _);

            var result = loadout.Validate(catalog);

            Assert.That(result.IsDeployable, Is.True);
            Assert.That(result.HasIssues, Is.False);
        }

        private static bool HasError(LoadoutValidation validation, ValidationCode code)
        {
            var issue = FindIssue(validation, code);
            return issue.HasValue && issue.Value.Severity == ValidationSeverity.Error;
        }

        private static bool HasIssue(LoadoutValidation validation, ValidationCode code)
        {
            return FindIssue(validation, code).HasValue;
        }

        private static ValidationIssue? FindIssue(LoadoutValidation validation, ValidationCode code)
        {
            for (int i = 0; i < validation.Issues.Count; i++)
            {
                if (validation.Issues[i].Code == code)
                {
                    return validation.Issues[i];
                }
            }
            return null;
        }
    }
}
