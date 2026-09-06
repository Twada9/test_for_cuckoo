using System;
using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using UnityEngine;

namespace ModularMech.UI
{
    /// <summary>
    /// 8スロットの一覧。スロット数はゲーム内で固定なので、エントリは起動時に1つずつ生成して
    /// 使い回す(装備変更のたびに Destroy/Instantiate しない)。
    /// </summary>
    public sealed class SlotListView : MonoBehaviour
    {
        [SerializeField] private SlotEntryView entryPrefab;
        [SerializeField] private Transform entryContainer;

        private readonly Dictionary<PartSlot, SlotEntryView> _entries = new Dictionary<PartSlot, SlotEntryView>();
        private PartSlot _selectedSlot;
        private bool _hasSelection;

        /// <summary>スロットがクリックされた。GarageScreen が選択状態とパーツ一覧の表示を仲介する。</summary>
        public event Action<PartSlot> SlotSelected;

        private void Awake()
        {
            if (entryPrefab == null || entryContainer == null)
            {
                Debug.LogError("SlotListView: entryPrefab / entryContainer が未設定です。", this);
                return;
            }

            foreach (var slot in PartSlots.All)
            {
                SlotEntryView entry = Instantiate(entryPrefab, entryContainer);
                entry.Initialize(slot, HandleEntryClicked);
                _entries[slot] = entry;
            }
        }

        /// <summary>指定スロットの表示だけを更新する。全体は作り直さない。</summary>
        public void RefreshSlot(PartSlot slot, IPartData equippedPart)
        {
            if (_entries.TryGetValue(slot, out SlotEntryView entry))
            {
                entry.SetContent(equippedPart);
            }
        }

        /// <summary>全スロットをまとめて再表示する。画面を開いた直後の初期化用。</summary>
        public void RefreshAll(Loadout loadout, IPartCatalog catalog)
        {
            foreach (var slot in PartSlots.All)
            {
                IPartData part = null;
                string partId = loadout?.GetPartId(slot);
                if (!string.IsNullOrEmpty(partId))
                {
                    catalog?.TryGet(partId, out part);
                }
                RefreshSlot(slot, part);
            }
        }

        public void SetSelected(PartSlot slot)
        {
            if (_hasSelection && _entries.TryGetValue(_selectedSlot, out SlotEntryView previous))
            {
                previous.SetSelected(false);
            }

            _selectedSlot = slot;
            _hasSelection = true;

            if (_entries.TryGetValue(slot, out SlotEntryView current))
            {
                current.SetSelected(true);
            }
        }

        private void HandleEntryClicked(PartSlot slot)
        {
            SetSelected(slot);
            SlotSelected?.Invoke(slot);
        }
    }
}
