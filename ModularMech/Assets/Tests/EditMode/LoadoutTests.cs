using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using NUnit.Framework;

namespace ModularMech.Tests
{
    /// <summary>
    /// Loadout の装備・解除・変更通知・複製を固定する(CLAUDE.md 共有コントラクト)。
    /// </summary>
    [TestFixture]
    public sealed class LoadoutTests
    {
        [Test]
        public void TryEquip_SlotMismatch_RejectsAndLeavesSlotEmpty()
        {
            var loadout = new Loadout();
            var arm = PartDataBuilder.ArmLeft();

            var ok = loadout.TryEquip(PartSlot.Torso, arm, out var error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.EqualTo(EquipError.SlotMismatch));
            Assert.That(loadout.IsEquipped(PartSlot.Torso), Is.False);
        }

        [Test]
        public void TryEquip_HandWithoutArm_RejectsWithMissingRequiredArm()
        {
            var loadout = new Loadout();
            var hand = PartDataBuilder.HandLeft();

            var ok = loadout.TryEquip(PartSlot.HandLeft, hand, out var error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.EqualTo(EquipError.MissingRequiredArm));
            Assert.That(loadout.IsEquipped(PartSlot.HandLeft), Is.False);
        }

        [Test]
        public void TryEquip_HandAfterArmEquipped_Succeeds()
        {
            var loadout = new Loadout();
            var arm = PartDataBuilder.ArmLeft();
            var hand = PartDataBuilder.HandLeft();

            Assert.That(loadout.TryEquip(PartSlot.ArmLeft, arm, out _), Is.True);
            var ok = loadout.TryEquip(PartSlot.HandLeft, hand, out var error);

            Assert.That(ok, Is.True);
            Assert.That(error, Is.EqualTo(EquipError.None));
            Assert.That(loadout.GetPartId(PartSlot.HandLeft), Is.EqualTo(hand.PartId));
        }

        [Test]
        public void TryEquip_NullPart_RejectsWithNullPart()
        {
            var loadout = new Loadout();

            var ok = loadout.TryEquip(PartSlot.Torso, null, out var error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.EqualTo(EquipError.NullPart));
        }

        [Test]
        public void TryEquip_PartWithEmptyId_RejectsWithNullPart()
        {
            var loadout = new Loadout();
            var badPart = PartDataBuilder.Part(string.Empty, PartSlot.Torso);

            var ok = loadout.TryEquip(PartSlot.Torso, badPart, out var error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.EqualTo(EquipError.NullPart));
        }

        [Test]
        public void TryEquip_NewSlot_FiresSlotChangedExactlyOnce()
        {
            var loadout = new Loadout();
            var fired = new List<PartSlot>();
            loadout.SlotChanged += fired.Add;

            var torso = PartDataBuilder.Torso();
            loadout.TryEquip(PartSlot.Torso, torso, out _);

            Assert.That(fired, Is.EqualTo(new[] { PartSlot.Torso }));
        }

        [Test]
        public void TryEquip_SamePartIdAgain_DoesNotFireSlotChanged()
        {
            var loadout = new Loadout();
            var torso = PartDataBuilder.Torso("torso_a");
            loadout.TryEquip(PartSlot.Torso, torso, out _);

            var fired = new List<PartSlot>();
            loadout.SlotChanged += fired.Add;

            // 同じ ID を付け直すだけ。実際には何も変わっていないので通知しない。
            var sameIdAgain = PartDataBuilder.Torso("torso_a");
            loadout.TryEquip(PartSlot.Torso, sameIdAgain, out _);

            Assert.That(fired, Is.Empty);
        }

        [Test]
        public void TryEquip_DifferentPartIdSameSlot_FiresSlotChangedOnce()
        {
            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.Torso, PartDataBuilder.Torso("torso_a"), out _);

            var fired = new List<PartSlot>();
            loadout.SlotChanged += fired.Add;

            loadout.TryEquip(PartSlot.Torso, PartDataBuilder.Torso("torso_b"), out _);

            Assert.That(fired, Is.EqualTo(new[] { PartSlot.Torso }));
        }

        [Test]
        public void TryEquip_RejectedEquip_NeverFiresSlotChanged()
        {
            var loadout = new Loadout();
            var fired = new List<PartSlot>();
            loadout.SlotChanged += fired.Add;

            loadout.TryEquip(PartSlot.Torso, PartDataBuilder.ArmLeft(), out _); // SlotMismatch
            loadout.TryEquip(PartSlot.HandLeft, PartDataBuilder.HandLeft(), out _); // MissingRequiredArm
            loadout.TryEquip(PartSlot.Torso, null, out _); // NullPart

            Assert.That(fired, Is.Empty);
        }

        [Test]
        public void Unequip_ArmLeftWithHandEquipped_AlsoRemovesHandLeft()
        {
            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.ArmLeft, PartDataBuilder.ArmLeft(), out _);
            loadout.TryEquip(PartSlot.HandLeft, PartDataBuilder.HandLeft(), out _);

            loadout.Unequip(PartSlot.ArmLeft);

            Assert.That(loadout.IsEquipped(PartSlot.ArmLeft), Is.False);
            Assert.That(loadout.IsEquipped(PartSlot.HandLeft), Is.False,
                "腕を外すと、その腕に依存する手も一緒に外れる(CLAUDE.md 共有コントラクト)");
        }

        [Test]
        public void Unequip_ArmLeftWithHandEquipped_FiresSlotChangedForBothSlotsInOrder()
        {
            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.ArmLeft, PartDataBuilder.ArmLeft(), out _);
            loadout.TryEquip(PartSlot.HandLeft, PartDataBuilder.HandLeft(), out _);

            var fired = new List<PartSlot>();
            loadout.SlotChanged += fired.Add;

            loadout.Unequip(PartSlot.ArmLeft);

            // 1回の Unequip 操作で2回発火する(D-6 が前提とする挙動そのもの)。
            Assert.That(fired, Is.EqualTo(new[] { PartSlot.ArmLeft, PartSlot.HandLeft }));
        }

        [Test]
        public void Unequip_ArmLeftWithoutHandEquipped_FiresSlotChangedOnlyOnce()
        {
            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.ArmLeft, PartDataBuilder.ArmLeft(), out _);

            var fired = new List<PartSlot>();
            loadout.SlotChanged += fired.Add;

            loadout.Unequip(PartSlot.ArmLeft);

            Assert.That(fired, Is.EqualTo(new[] { PartSlot.ArmLeft }));
        }

        [Test]
        public void Unequip_SlotNotEquipped_NoEventFiredAndStateUnchanged()
        {
            var loadout = new Loadout();
            var fired = new List<PartSlot>();
            loadout.SlotChanged += fired.Add;

            loadout.Unequip(PartSlot.Torso);

            Assert.That(fired, Is.Empty);
        }

        [Test]
        public void Clone_CopiesNameAndEquippedSlots()
        {
            var original = new Loadout("original");
            original.TryEquip(PartSlot.Torso, PartDataBuilder.Torso("torso_a"), out _);
            original.TryEquip(PartSlot.Legs, PartDataBuilder.BipedLegs("legs_a"), out _);

            var clone = original.Clone();

            Assert.That(clone.Name, Is.EqualTo("original"));
            Assert.That(clone.GetPartId(PartSlot.Torso), Is.EqualTo("torso_a"));
            Assert.That(clone.GetPartId(PartSlot.Legs), Is.EqualTo("legs_a"));
        }

        [Test]
        public void Clone_IsIndependentCopy_MutatingCloneDoesNotAffectOriginal()
        {
            var original = new Loadout("original");
            original.TryEquip(PartSlot.Torso, PartDataBuilder.Torso("torso_a"), out _);

            var clone = original.Clone();
            clone.TryEquip(PartSlot.Torso, PartDataBuilder.Torso("torso_b"), out _);
            clone.Unequip(PartSlot.Torso);

            Assert.That(original.GetPartId(PartSlot.Torso), Is.EqualTo("torso_a"),
                "クローンへの変更が元の Loadout に波及してはならない(深いコピー)");
        }

        [Test]
        public void Clone_DoesNotCarryOverEventSubscribers()
        {
            var original = new Loadout("original");
            var originalFireCount = 0;
            original.SlotChanged += _ => originalFireCount++;

            var clone = original.Clone();
            clone.TryEquip(PartSlot.Torso, PartDataBuilder.Torso(), out _);
            clone.Unequip(PartSlot.Torso);

            Assert.That(originalFireCount, Is.EqualTo(0),
                "クローンはイベント購読を引き継がない(元の UI を巻き込んではいけない)");
        }
    }
}
