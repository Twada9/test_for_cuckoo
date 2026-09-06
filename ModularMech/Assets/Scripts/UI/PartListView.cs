using System;
using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using UnityEngine;

namespace ModularMech.UI
{
    /// <summary>
    /// 選択中スロットに装備可能なパーツ一覧(<see cref="PartCatalog.PartsForSlot"/>)。
    /// エントリはプールし、スロット切り替えのたびに Destroy/Instantiate しない
    /// (必要数だけ有効化し、余りは非表示のまま使い回す)。
    /// </summary>
    public sealed class PartListView : MonoBehaviour
    {
        [SerializeField] private PartEntryView entryPrefab;
        [SerializeField] private Transform entryContainer;

        private readonly List<PartEntryView> _pool = new List<PartEntryView>();

        /// <summary>パーツがクリックされた(装備可能かどうかに関わらず、非活性ボタンはそもそもクリックできない)。</summary>
        public event Action<PartDefinition> PartChosen;

        /// <summary>
        /// 指定スロットの装備可能パーツ一覧を表示する。ArmLeft/ArmRight の有無で HandLeft/HandRight の
        /// 装備可否が変わるため、呼び出し側(GarageScreen)は該当スロットの装備が変わるたびに呼び直す。
        /// </summary>
        public void Show(PartSlot slot, PartCatalog catalog, Loadout loadout)
        {
            if (catalog == null)
            {
                HideFrom(0);
                return;
            }

            IReadOnlyList<PartDefinition> parts = catalog.PartsForSlot(slot);
            string equippedId = loadout?.GetPartId(slot);

            EnsurePoolSize(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                PartDefinition part = parts[i];
                PartEntryView entry = _pool[i];
                entry.SetActiveEntry(true);

                bool equippable = CanEquip(slot, part, loadout, out string reason);
                bool isEquipped = part != null && !string.IsNullOrEmpty(equippedId) && part.partId == equippedId;
                entry.SetContent(part, equippable, reason, isEquipped);
            }

            HideFrom(parts.Count);
        }

        private bool CanEquip(PartSlot slot, PartDefinition part, Loadout loadout, out string reason)
        {
            reason = null;

            if (part == null)
            {
                reason = "パーツが見つかりません。";
                return false;
            }

            // 通常は PartsForSlot が既にスロットで絞り込んでいるため発生しないはずの防御チェック。
            if (part.slot != slot)
            {
                reason = "このスロットには装備できません。";
                return false;
            }

            // HandRequiresArm(設計ドキュメント §3.2): 手持ちは対応する腕が無いと装備できない。
            // Loadout.TryEquip でも同じ規則が最終的に検証されるが、クリック前にボタンを非活性にして
            // 理由を見せるため、ここでは装備を試みずに Loadout の現在状態だけを見て判定する。
            if (loadout != null && PartSlots.TryGetRequiredArm(slot, out PartSlot armSlot))
            {
                string armId = loadout.GetPartId(armSlot);
                if (string.IsNullOrEmpty(armId))
                {
                    reason = $"{ArmDisplayName(armSlot)}を装備していないと装備できません。";
                    return false;
                }
            }

            return true;
        }

        private void EnsurePoolSize(int count)
        {
            if (entryPrefab == null || entryContainer == null)
            {
                return;
            }

            while (_pool.Count < count)
            {
                PartEntryView entry = Instantiate(entryPrefab, entryContainer);
                entry.Initialize(HandleEntryClicked);
                _pool.Add(entry);
            }
        }

        private void HideFrom(int fromIndex)
        {
            for (int i = fromIndex; i < _pool.Count; i++)
            {
                _pool[i].SetActiveEntry(false);
            }
        }

        private void HandleEntryClicked(PartDefinition part)
        {
            PartChosen?.Invoke(part);
        }

        private static string ArmDisplayName(PartSlot armSlot)
        {
            return armSlot == PartSlot.ArmLeft ? "左腕" : "右腕";
        }
    }
}
