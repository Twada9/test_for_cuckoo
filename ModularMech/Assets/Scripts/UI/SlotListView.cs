using System;
using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using UnityEngine;

namespace ModularMech.UI
{
    /// <summary>
    /// 8スロットの一覧。スロット数はゲーム内で固定なので、エントリは1つずつ生成して
    /// 使い回す(装備変更のたびに Destroy/Instantiate しない)。
    ///
    /// <para>
    /// エントリ生成は <c>Awake</c> ではなく <see cref="EnsureEntries"/> の遅延初期化で行う
    /// (CLAUDE.md D-16)。Awake に置くと、他オブジェクトの OnEnable(GarageScreen 等)から
    /// このビューの公開メソッドが先に呼ばれたとき、エントリが空のまま黙って何も表示せずに終わる。
    /// 公開メソッドはすべて先頭で <see cref="EnsureEntries"/> を呼ぶこと。
    /// </para>
    /// </summary>
    public sealed class SlotListView : MonoBehaviour
    {
        [SerializeField] private SlotEntryView entryPrefab;
        [SerializeField] private Transform entryContainer;

        private readonly Dictionary<PartSlot, SlotEntryView> _entries = new Dictionary<PartSlot, SlotEntryView>();
        private PartSlot _selectedSlot;
        private bool _hasSelection;
        private bool _entriesBuilt;

        /// <summary>スロットがクリックされた。GarageScreen が選択状態とパーツ一覧の表示を仲介する。</summary>
        public event Action<PartSlot> SlotSelected;

        /// <summary>
        /// エントリをまだ作っていなければ作る。呼び出し順に依存せず、何度呼んでも安全。
        /// 未配線で失敗した場合も「試行済み」にする ―― 公開メソッドのたびに
        /// 同じ LogError を撒くと、本当のエラーがコンソールから流れてしまうため。
        /// </summary>
        private void EnsureEntries()
        {
            if (_entriesBuilt)
            {
                return;
            }

            _entriesBuilt = true;

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
            EnsureEntries();

            if (_entries.TryGetValue(slot, out SlotEntryView entry))
            {
                entry.SetContent(equippedPart);
            }
        }

        /// <summary>全スロットをまとめて再表示する。画面を開いた直後の初期化用。</summary>
        public void RefreshAll(Loadout loadout, IPartCatalog catalog)
        {
            EnsureEntries();

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
            EnsureEntries();

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
