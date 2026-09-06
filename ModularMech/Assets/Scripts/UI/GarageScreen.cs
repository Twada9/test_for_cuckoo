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
    /// 装備変更のたびに画面全体を作り直すことはしない。<see cref="Loadout.SlotChanged"/> は
    /// UI の差分更新(スロット表示・パーツ一覧の活性状態)専用として使い、ここから直接
    /// MechRuntime.Apply は呼ばない(CLAUDE.md D-6)。リビルドそのものの1回への集約は
    /// <see cref="MechRuntime"/> 自身が同じ Loadout.SlotChanged を購読して既に行っている
    /// (RequestRebuild + 自身の LateUpdate)ため、ここで二重に dirty flag を持つと
    /// 1フレームに2回 Apply が走ってしまう。GarageScreen が明示的に Apply を呼ぶのは、
    /// working Loadout を MechRuntime の ActiveLoadout として認識させる最初の1回と、
    /// Load で Loadout インスタンスそのものを丸ごと差し替えたときだけでよい。
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

        /// <summary>
        /// 出撃可否(テストフィールド遷移ボタン等、他画面から参照する想定のフック)。
        /// MechRuntime.Validation は LoadoutApplied のときだけ再生成されるキャッシュ済みの結果を返す
        /// (D-10)ので、ここで新たに Validate を呼び直すことはしない。
        /// </summary>
        public bool CanDeploy => mechRuntime != null
            && mechRuntime.Validation != null
            && mechRuntime.Validation.IsDeployable;

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
            SelectSlot(PartSlot.Torso);

            // MechRuntime に working Loadout を ActiveLoadout として認識させる最初の1回。
            // 以後のスロット変更は Loadout.SlotChanged 経由で MechRuntime 自身がまとめて再適用する。
            ApplyWorkingLoadout();
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

        /// <summary>
        /// PartListView からの選択。part が null のときは「装備しない」選択肢(先頭のエントリ)であり、
        /// スロットを空にする(design doc §2.1: Torso/Legs 以外は null を許容。Torso/Legs 自体を
        /// 空にすることも Loadout.Unequip は拒否しない — 結果は Validation が Error として警告する)。
        /// </summary>
        private void HandlePartChosen(PartDefinition part)
        {
            if (!_hasSelectedSlot || _workingLoadout == null)
            {
                return;
            }

            if (part == null)
            {
                _workingLoadout.Unequip(_selectedSlot);
                return; // MechRuntime が Loadout.SlotChanged を購読して自分でリビルドをまとめる(D-6)。
            }

            if (!_workingLoadout.TryEquip(_selectedSlot, part, out EquipError error))
            {
                // PartListView 側で装備不可なものは非活性にしているため、通常はここに来ない。
                // カタログ不整合など想定外の経路に備えて警告だけ残す。
                Debug.LogWarning($"[GarageScreen] 装備に失敗しました: スロット={_selectedSlot}, パーツ={part.partId}, 理由={error}");
            }
            // 成功時も、ここから直接 Apply は呼ばない(D-6)。MechRuntime 自身が処理する。
        }

        /// <summary>Loadout.SlotChanged: 変化したスロットの表示だけを軽量に更新する(UI差分更新専用)。</summary>
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

            statPanelView.Refresh(
                mechRuntime.CurrentStats,
                mechRuntime.Validation,
                mechRuntime.Locomotion,
                ComputeRawCapabilities());
        }

        /// <summary>
        /// ペナルティ適用前の「本来付与されている」能力の合計。StatBlock.Capabilities は
        /// ペナルティ適用後の値しか持たないため、UI が「そもそも付与されていない」と
        /// 「付与されたが剥奪された」を区別できるよう(D-9)、ここだけ独自に集約し直す。
        ///
        /// <see cref="LoadoutResolver.Resolve"/> は CLAUDE.md の確定済み契約にある呼び出しのみを
        /// 使う(MechRuntime が内部で保持しているかもしれない解決結果のキャッシュには依存しない —
        /// そちらは契約に明記された公開APIではないため)。LoadoutApplied 時にしか呼ばないので、
        /// 毎フレームの再解決にはならない(D-10 の趣旨に反しない)。
        /// </summary>
        private CapabilityFlags ComputeRawCapabilities()
        {
            if (partCatalog == null || _workingLoadout == null)
            {
                return CapabilityFlags.None;
            }

            ResolvedLoadout resolved = LoadoutResolver.Resolve(_workingLoadout, partCatalog);
            CapabilityFlags raw = CapabilityFlags.None;
            foreach (IPartData part in resolved.All)
            {
                raw |= part.GrantedCapabilities;
            }
            return raw;
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
            if (_hasSelectedSlot)
            {
                partListView?.Show(_selectedSlot, partCatalog, _workingLoadout);
            }

            // Loadout インスタンスを丸ごと差し替えたので、MechRuntime に明示的に再認識させる。
            ApplyWorkingLoadout();
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
