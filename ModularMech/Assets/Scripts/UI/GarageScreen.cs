using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using ModularMech.Mechs;
using UnityEngine;
using UnityEngine.UI;

namespace ModularMech.UI
{
    /// <summary>
    /// ガレージ画面の司令塔。スロット選択 → 装備可能パーツ一覧 → 装備 → MechRuntime.Apply →
    /// プレビュー即時反映 → ステータス更新、の一連を仲介する。
    ///
    /// <para>
    /// 装備変更のたびに画面全体を作り直すことはしない。<see cref="Loadout.SlotChanged"/> で
    /// 変化したスロットの表示だけを、<see cref="MechRuntime.LoadoutApplied"/> でステータスパネルだけを
    /// それぞれ更新する。3Dプレビュー自体の差分リビルドは MechRuntime.Apply の内部
    /// (MechAssembly.Rebuild)が担当するため、ここでは触れない。
    /// </para>
    /// </summary>
    public sealed class GarageScreen : MonoBehaviour
    {
        [Header("ランタイム / データ")]
        [SerializeField] private MechRuntime mechRuntime;
        [SerializeField] private PartCatalog partCatalog;

        [Header("子ビュー")]
        [SerializeField] private SlotListView slotListView;
        [SerializeField] private PartListView partListView;
        [SerializeField] private StatPanelView statPanelView;

        [Header("保存/読込(任意。ボタンを繋がない場合は使わない)")]
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;

        private Loadout _workingLoadout;
        private PartSlot _selectedSlot;
        private bool _hasSelectedSlot;

        private void OnEnable()
        {
            EnsureWorkingLoadout();

            if (slotListView != null) slotListView.SlotSelected += HandleSlotSelected;
            if (partListView != null) partListView.PartChosen += HandlePartChosen;
            if (mechRuntime != null) mechRuntime.LoadoutApplied += HandleLoadoutApplied;
            if (_workingLoadout != null) _workingLoadout.SlotChanged += HandleSlotChanged;

            if (saveButton != null)
            {
                saveButton.onClick.RemoveListener(SaveToDisk);
                saveButton.onClick.AddListener(SaveToDisk);
            }
            if (loadButton != null)
            {
                loadButton.onClick.RemoveListener(LoadFromDisk);
                loadButton.onClick.AddListener(LoadFromDisk);
            }

            RefreshSlotList();
            ApplyWorkingLoadout();
            SelectSlot(PartSlot.Torso);
        }

        private void OnDisable()
        {
            if (slotListView != null) slotListView.SlotSelected -= HandleSlotSelected;
            if (partListView != null) partListView.PartChosen -= HandlePartChosen;
            if (mechRuntime != null) mechRuntime.LoadoutApplied -= HandleLoadoutApplied;
            if (_workingLoadout != null) _workingLoadout.SlotChanged -= HandleSlotChanged;

            if (saveButton != null) saveButton.onClick.RemoveListener(SaveToDisk);
            if (loadButton != null) loadButton.onClick.RemoveListener(LoadFromDisk);
        }

        /// <summary>
        /// 初回は MechRuntime が既に持っている構成を引き継ぎ、無ければ空の Loadout から始める。
        /// 2回目以降の OnEnable では作り直さない(この画面が非表示の間に外部から
        /// ActiveLoadout が差し替えられるケースまでは v1 では追従しない — 未検証点として報告する)。
        /// </summary>
        private void EnsureWorkingLoadout()
        {
            if (_workingLoadout != null)
            {
                return;
            }

            _workingLoadout = mechRuntime != null && mechRuntime.ActiveLoadout != null
                ? mechRuntime.ActiveLoadout.Clone()
                : new Loadout();
        }

        private void HandleSlotSelected(PartSlot slot)
        {
            SelectSlot(slot);
        }

        private void SelectSlot(PartSlot slot)
        {
            _selectedSlot = slot;
            _hasSelectedSlot = true;
            slotListView?.SetSelected(slot);
            partListView?.Show(slot, partCatalog, _workingLoadout);
        }

        private void HandlePartChosen(PartDefinition part)
        {
            if (part == null || !_hasSelectedSlot || _workingLoadout == null)
            {
                return;
            }

            if (!_workingLoadout.TryEquip(_selectedSlot, part, out EquipError error))
            {
                // PartListView 側で装備不可なものは非活性にしているため、通常はここに来ない。
                // カタログ不整合など想定外の経路に備えて警告だけ残す。
                Debug.LogWarning($"[GarageScreen] 装備に失敗しました: スロット={_selectedSlot}, パーツ={part.partId}, 理由={error}");
                return;
            }

            ApplyWorkingLoadout();
        }

        /// <summary>Loadout.SlotChanged: 変化したスロットの表示だけを軽量に更新する。</summary>
        private void HandleSlotChanged(PartSlot slot)
        {
            IPartData part = null;
            string partId = _workingLoadout.GetPartId(slot);
            if (!string.IsNullOrEmpty(partId))
            {
                partCatalog?.TryGet(partId, out part);
            }
            slotListView?.RefreshSlot(slot, part);

            // 腕の着脱は HandRequiresArm を通じて手持ちスロットの装備可否に影響するため、
            // 選択中スロットのパーツ一覧(ボタンの活性状態)を更新する。
            if (_hasSelectedSlot)
            {
                partListView?.Show(_selectedSlot, partCatalog, _workingLoadout);
            }
        }

        /// <summary>MechRuntime.LoadoutApplied: ステータスパネルだけを更新する。</summary>
        private void HandleLoadoutApplied()
        {
            if (mechRuntime == null || statPanelView == null)
            {
                return;
            }
            statPanelView.Refresh(mechRuntime.CurrentStats, mechRuntime.Validation, mechRuntime.Locomotion);
        }

        private void ApplyWorkingLoadout()
        {
            if (mechRuntime == null || partCatalog == null)
            {
                return;
            }
            // Apply 内部で MechAssembly.Rebuild とアニメータ差し替え、LoadoutApplied の発火まで行われる
            // (設計ドキュメント §5.1)。ここでは呼ぶだけでよい。
            mechRuntime.Apply(_workingLoadout, partCatalog);
        }

        private void RefreshSlotList()
        {
            slotListView?.RefreshAll(_workingLoadout, partCatalog);
        }

        /// <summary>現在の作業中Loadoutを1件構成として保存する(M7)。Save/Loadボタンから呼ぶ想定。</summary>
        public void SaveToDisk()
        {
            if (_workingLoadout == null)
            {
                return;
            }

            LoadoutSaveResult result = LoadoutSaveFile.Save(new List<Loadout> { _workingLoadout }, 0);
            LogWarnings(result.Warnings);
        }

        /// <summary>保存ファイルを読み込み、先頭(activeIndex)の構成を作業中Loadoutとして反映する(M7)。</summary>
        public void LoadFromDisk()
        {
            if (partCatalog == null)
            {
                return;
            }

            LoadoutLoadResult result = LoadoutSaveFile.Load(partCatalog);
            LogWarnings(result.Warnings);

            if (!result.Success || result.Loadouts == null || result.Loadouts.Count == 0)
            {
                return;
            }

            int index = Mathf.Clamp(result.ActiveIndex, 0, result.Loadouts.Count - 1);

            if (_workingLoadout != null)
            {
                _workingLoadout.SlotChanged -= HandleSlotChanged;
            }
            _workingLoadout = result.Loadouts[index];
            _workingLoadout.SlotChanged += HandleSlotChanged;

            RefreshSlotList();
            ApplyWorkingLoadout();
            if (_hasSelectedSlot)
            {
                partListView?.Show(_selectedSlot, partCatalog, _workingLoadout);
            }
        }

        private static void LogWarnings(IReadOnlyList<string> warnings)
        {
            if (warnings == null)
            {
                return;
            }
            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning($"[GarageScreen] {warnings[i]}");
            }
        }
    }
}
