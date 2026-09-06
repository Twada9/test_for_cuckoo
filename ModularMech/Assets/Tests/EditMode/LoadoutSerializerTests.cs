using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using ModularMech.Serialization;
using NUnit.Framework;

namespace ModularMech.Tests
{
    /// <summary>
    /// LoadoutSerializer を設計ドキュメント §7 の JSON 形に厳密一致させて固定する。
    /// 形の確認は MiniJson.Deserialize で読み戻した木を見て行う(生文字列の比較は
    /// キー順序に依存して壊れやすいため避ける)。
    /// </summary>
    [TestFixture]
    public sealed class LoadoutSerializerTests
    {
        [Test]
        public void Serialize_OutputShape_MatchesDesignDocumentSection7()
        {
            var torso = PartDataBuilder.Torso("torso_light_01");
            var legs = PartDataBuilder.BipedLegs("legs_biped_01");

            var loadout = new Loadout("デフォルト機");
            loadout.TryEquip(PartSlot.Torso, torso, out _);
            loadout.TryEquip(PartSlot.Legs, legs, out _);
            // Backpack などその他のスロットは未装備のまま(§7 の例と同じ形)。

            var json = LoadoutSerializer.Serialize(new List<Loadout> { loadout }, activeIndex: 0);
            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;

            Assert.That(root, Is.Not.Null);
            Assert.That(root["version"], Is.EqualTo((double)LoadoutSerializer.CurrentVersion));
            Assert.That(root["activeLoadoutIndex"], Is.EqualTo(0.0));

            var loadoutsArray = root["loadouts"] as List<object>;
            Assert.That(loadoutsArray, Is.Not.Null);
            Assert.That(loadoutsArray.Count, Is.EqualTo(1));

            var entry = loadoutsArray[0] as Dictionary<string, object>;
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry["name"], Is.EqualTo("デフォルト機"));

            var parts = entry["parts"] as Dictionary<string, object>;
            Assert.That(parts, Is.Not.Null);

            // §7 の例に出てくるキー全てが、8スロット分そのまま出ること。
            foreach (var slot in PartSlots.All)
            {
                Assert.That(parts.ContainsKey(slot.ToString()), Is.True, $"スロット {slot} のキーが出力に無い");
            }

            Assert.That(parts["Torso"], Is.EqualTo("torso_light_01"));
            Assert.That(parts["Legs"], Is.EqualTo("legs_biped_01"));
        }

        [Test]
        public void Serialize_UnequippedSlots_AreWrittenAsNull()
        {
            var loadout = new Loadout("空機体");
            // どのスロットも装備しない。

            var json = LoadoutSerializer.Serialize(new List<Loadout> { loadout }, activeIndex: 0);
            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            var entry = ((List<object>)root["loadouts"])[0] as Dictionary<string, object>;
            var parts = entry["parts"] as Dictionary<string, object>;

            foreach (var slot in PartSlots.All)
            {
                Assert.That(parts[slot.ToString()], Is.Null, $"{slot} は未装備なので null であるべき");
            }
        }

        [Test]
        public void Deserialize_UnknownPartId_LeavesSlotEmptyAndAddsWarning()
        {
            var torso = PartDataBuilder.Torso("torso_light_01");
            var loadoutAtSaveTime = new Loadout("旧構成");
            loadoutAtSaveTime.TryEquip(PartSlot.Torso, torso, out _);
            var ghostLegs = PartDataBuilder.BipedLegs("legs_discontinued");
            loadoutAtSaveTime.TryEquip(PartSlot.Legs, ghostLegs, out _);

            var json = LoadoutSerializer.Serialize(new List<Loadout> { loadoutAtSaveTime }, activeIndex: 0);

            // 読み込み時のカタログには legs_discontinued がもう無い。
            var catalogAtLoadTime = PartDataBuilder.Catalog(torso);
            var result = LoadoutSerializer.Deserialize(json, catalogAtLoadTime);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Loadouts.Count, Is.EqualTo(1));
            Assert.That(result.Loadouts[0].GetPartId(PartSlot.Legs), Is.Null,
                "カタログに無い ID のスロットは空にして復元する(§7)");
            Assert.That(result.Loadouts[0].GetPartId(PartSlot.Torso), Is.EqualTo("torso_light_01"));
            Assert.That(result.Warnings, Is.Not.Empty);
            Assert.That(result.Warnings, Has.Some.Contains("legs_discontinued"));
        }

        [Test]
        public void Deserialize_ActiveIndexNegative_ClampedToZeroWithWarning()
        {
            var torso = PartDataBuilder.Torso();
            var loadout = new Loadout("a");
            var catalog = PartDataBuilder.Catalog(torso);
            loadout.TryEquip(PartSlot.Torso, torso, out _);

            var json = LoadoutSerializer.Serialize(new List<Loadout> { loadout }, activeIndex: -1);
            var result = LoadoutSerializer.Deserialize(json, catalog);

            Assert.That(result.ActiveIndex, Is.EqualTo(0));
            Assert.That(result.Warnings, Has.Some.Contains("activeLoadoutIndex"));
        }

        [Test]
        public void Deserialize_ActiveIndexOutOfRangeHigh_ClampedToZeroWithWarning()
        {
            var torso = PartDataBuilder.Torso();
            var loadout = new Loadout("a");
            var catalog = PartDataBuilder.Catalog(torso);
            loadout.TryEquip(PartSlot.Torso, torso, out _);

            var json = LoadoutSerializer.Serialize(new List<Loadout> { loadout }, activeIndex: 5);
            var result = LoadoutSerializer.Deserialize(json, catalog);

            Assert.That(result.ActiveIndex, Is.EqualTo(0));
            Assert.That(result.Warnings, Has.Some.Contains("activeLoadoutIndex"));
        }

        [Test]
        public void Deserialize_ActiveIndexInRange_NotClampedAndNoIndexWarning()
        {
            var torso = PartDataBuilder.Torso();
            var catalog = PartDataBuilder.Catalog(torso);

            var first = new Loadout("a");
            first.TryEquip(PartSlot.Torso, torso, out _);
            var second = new Loadout("b");
            second.TryEquip(PartSlot.Torso, torso, out _);

            var json = LoadoutSerializer.Serialize(new List<Loadout> { first, second }, activeIndex: 1);
            var result = LoadoutSerializer.Deserialize(json, catalog);

            Assert.That(result.ActiveIndex, Is.EqualTo(1));
            Assert.That(result.Warnings, Is.Empty, "有効な構成のみで、範囲内インデックスなら警告は出ない");
        }

        [Test]
        public void Deserialize_UnknownVersion_StillParsesButAddsWarning()
        {
            var torso = PartDataBuilder.Torso("torso_light_01");
            var catalog = PartDataBuilder.Catalog(torso);

            var root = new Dictionary<string, object>
            {
                ["version"] = 999.0,
                ["loadouts"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "未来バージョン機",
                        ["parts"] = new Dictionary<string, object> { ["Torso"] = "torso_light_01" },
                    },
                },
                ["activeLoadoutIndex"] = 0.0,
            };
            var json = MiniJson.Serialize(root);

            var result = LoadoutSerializer.Deserialize(json, catalog);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Loadouts.Count, Is.EqualTo(1));
            Assert.That(result.Loadouts[0].GetPartId(PartSlot.Torso), Is.EqualTo("torso_light_01"));
            Assert.That(result.Warnings, Has.Some.Contains("999"));
        }

        [Test]
        public void Deserialize_MissingVersionKey_StillParsesButAddsWarning()
        {
            var torso = PartDataBuilder.Torso("torso_light_01");
            var catalog = PartDataBuilder.Catalog(torso);

            var root = new Dictionary<string, object>
            {
                // "version" キーを意図的に含めない。
                ["loadouts"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "バージョン無し機",
                        ["parts"] = new Dictionary<string, object> { ["Torso"] = "torso_light_01" },
                    },
                },
                ["activeLoadoutIndex"] = 0.0,
            };
            var json = MiniJson.Serialize(root);

            var result = LoadoutSerializer.Deserialize(json, catalog);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Warnings, Has.Some.Contains("version"));
        }

        [Test]
        public void Deserialize_MalformedJson_ReturnsFailureResultWithoutThrowing()
        {
            LoadoutLoadResult result = null;
            Assert.DoesNotThrow(() =>
                result = LoadoutSerializer.Deserialize("{not valid json", PartDataBuilder.Catalog()));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Loadouts, Is.Empty);
            Assert.That(result.ActiveIndex, Is.EqualTo(0));
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void Deserialize_EmptyString_ReturnsFailureResultWithWarning()
        {
            var result = LoadoutSerializer.Deserialize(string.Empty, PartDataBuilder.Catalog());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void RoundTrip_SerializeThenDeserialize_PreservesNameAndAllSlotAssignments()
        {
            var torso = PartDataBuilder.Torso("torso_light_01");
            var legs = PartDataBuilder.BipedLegs("legs_biped_01");
            var armLeft = PartDataBuilder.ArmLeft("arm_standard_l");
            var hand = PartDataBuilder.HandLeft("hand_tool_l");
            var catalog = PartDataBuilder.Catalog(torso, legs, armLeft, hand);

            var original = new Loadout("周回機");
            original.TryEquip(PartSlot.Torso, torso, out _);
            original.TryEquip(PartSlot.Legs, legs, out _);
            original.TryEquip(PartSlot.ArmLeft, armLeft, out _);
            original.TryEquip(PartSlot.HandLeft, hand, out _);
            // Head / ArmRight / Backpack / HandRight は未装備のまま。

            var json = LoadoutSerializer.Serialize(new List<Loadout> { original }, activeIndex: 0);
            var result = LoadoutSerializer.Deserialize(json, catalog);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Warnings, Is.Empty);
            var restored = result.Loadouts[0];

            Assert.That(restored.Name, Is.EqualTo("周回機"));
            foreach (var slot in PartSlots.All)
            {
                Assert.That(restored.GetPartId(slot), Is.EqualTo(original.GetPartId(slot)),
                    $"スロット {slot} が往復で一致しない");
            }
        }
    }
}
