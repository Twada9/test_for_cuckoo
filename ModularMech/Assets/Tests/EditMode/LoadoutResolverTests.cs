using ModularMech.Data;
using ModularMech.Loadouts;
using NUnit.Framework;

namespace ModularMech.Tests
{
    /// <summary>
    /// LoadoutResolver.Resolve を固定する。核となる性質は「未知 ID で例外を投げない」
    /// (設計ドキュメント §7 のアセット差し替え耐性)。
    /// </summary>
    [TestFixture]
    public sealed class LoadoutResolverTests
    {
        [Test]
        public void Resolve_AllPartsKnown_AllSlotsPopulatedAndNoneMissing()
        {
            var torso = PartDataBuilder.Torso();
            var legs = PartDataBuilder.BipedLegs();
            var catalog = PartDataBuilder.Catalog(torso, legs);

            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.Torso, torso, out _);
            loadout.TryEquip(PartSlot.Legs, legs, out _);

            var resolved = LoadoutResolver.Resolve(loadout, catalog);

            Assert.That(resolved.Torso, Is.SameAs(torso));
            Assert.That(resolved.Legs, Is.SameAs(legs));
            Assert.That(resolved.HasMissingParts, Is.False);
            Assert.That(resolved.MissingPartIds, Is.Empty);
        }

        [Test]
        public void Resolve_UnknownPartId_LeavesSlotEmptyAndRecordsMissing_NoException()
        {
            var torso = PartDataBuilder.Torso();
            var catalog = PartDataBuilder.Catalog(torso); // legs_biped_01 はカタログに無い

            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.Torso, torso, out _);
            // Legs は TryEquip 経由ではなく、直接カタログに存在しない ID を積んだ状態を作れないため、
            // 「一度カタログにあった状態で装備してから、カタログを差し替える」形で未知 ID を再現する。
            var legsAtEquipTime = PartDataBuilder.BipedLegs("legs_ghost");
            loadout.TryEquip(PartSlot.Legs, legsAtEquipTime, out _);

            ResolvedLoadout resolved = null;
            Assert.DoesNotThrow(() => resolved = LoadoutResolver.Resolve(loadout, catalog));

            Assert.That(resolved.Legs, Is.Null, "カタログに無い ID のスロットは未装備として扱う");
            Assert.That(resolved.TryGet(PartSlot.Legs, out _), Is.False);
            Assert.That(resolved.MissingPartIds, Does.Contain("legs_ghost"));
            Assert.That(resolved.MissingBySlot[PartSlot.Legs], Is.EqualTo("legs_ghost"));
        }

        [Test]
        public void Resolve_MultipleUnknownIds_MissingPartIdsOrderedBySlotDeclarationOrder()
        {
            // PartSlots.All の宣言順: Head, Torso, ArmLeft, ArmRight, Legs, Backpack, HandLeft, HandRight。
            // Legs と Head を未知 ID にして、MissingPartIds が Head -> Legs の順になることを確認する。
            var known = PartDataBuilder.Torso();
            var catalog = PartDataBuilder.Catalog(known);

            var ghostHead = PartDataBuilder.Head("head_ghost");
            var ghostLegs = PartDataBuilder.BipedLegs("legs_ghost");

            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.Legs, ghostLegs, out _);
            loadout.TryEquip(PartSlot.Head, ghostHead, out _);
            loadout.TryEquip(PartSlot.Torso, known, out _);

            var resolved = LoadoutResolver.Resolve(loadout, catalog);

            Assert.That(resolved.MissingPartIds, Is.EqualTo(new[] { "head_ghost", "legs_ghost" }));
        }

        [Test]
        public void Resolve_CatalogNull_AllEquippedSlotsBecomeMissing_NoException()
        {
            var loadout = new Loadout();
            var torso = PartDataBuilder.Torso();
            loadout.TryEquip(PartSlot.Torso, torso, out _);

            ResolvedLoadout resolved = null;
            Assert.DoesNotThrow(() => resolved = LoadoutResolver.Resolve(loadout, null));

            Assert.That(resolved.Torso, Is.Null);
            Assert.That(resolved.MissingPartIds, Does.Contain(torso.PartId));
        }

        [Test]
        public void Resolve_LoadoutNull_ReturnsEmptyResolvedLoadout_NoException()
        {
            ResolvedLoadout resolved = null;
            Assert.DoesNotThrow(() => resolved = LoadoutResolver.Resolve(null, PartDataBuilder.Catalog()));

            Assert.That(resolved.Torso, Is.Null);
            Assert.That(resolved.Legs, Is.Null);
            Assert.That(resolved.MissingPartIds, Is.Empty);
            Assert.That(resolved.Parts, Is.Empty);
        }

        [Test]
        public void Resolve_NoLegsEquipped_LegsPropertyIsNull()
        {
            var torso = PartDataBuilder.Torso();
            var catalog = PartDataBuilder.Catalog(torso);
            var loadout = new Loadout();
            loadout.TryEquip(PartSlot.Torso, torso, out _);

            var resolved = LoadoutResolver.Resolve(loadout, catalog);

            Assert.That(resolved.Legs, Is.Null);
            Assert.That(resolved.Locomotion, Is.Null);
        }
    }
}
