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

        [Header("プレビュー")]
        [Tooltip("ガレージのプレビュー機体の移動制御。未設定なら MechRuntime と同じ GameObject から拾う。\n" +
                 "ガレージでは必ず無効化する(D-19)。有効なままだと重力で落下し、" +
                 "MechPreviewRotator のドラッグ回転と移動戦略のヨー回転が同じ Transform を奪い合う。")]
        [SerializeField] private MechLocomotionController previewLocomotion;

        [Header("保存/読込(任意。ボタンを繋がない場合は使わない)")]
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;

        private Loadout _workingLoadout;
        private PartSlot _selectedSlot;
        private bool _hasSelectedSlot;
        private bool _initialized;

        /// <summary>
        /// 出撃可否(テストフィールド遷移ボタン等、他画面から参照する想定のフック)。
        /// MechRuntime.Validation は LoadoutApplied のときだけ再生成されるキャッシュ済みの結果を返す
        /// (D-10)ので、ここで新たに Validate を呼び直すことはしない。
        /// </summary>
        public bool CanDeploy => mechRuntime != null
            && mechRuntime.Validation != null
            && mechRuntime.Validation.IsDeployable;

        /// <summary>
        /// 購読だけを行う。状態の初期化は <see cref="Start"/> に置く(CLAUDE.md D-16)。
        ///
        /// <para>
        /// ここで初期化してはいけない理由: Unity は Awake → OnEnable → Start の順に回すため、
        /// OnEnable の時点では同じシーンの <see cref="MechRuntime"/> がまだ Start を通っておらず、
        /// <c>ActiveLoadout</c> は null のままである。そこで働き用 Loadout を作ってしまうと
        /// 「空の構成」を掴んでしまい、しかもそれを Apply することで MechRuntime 側の
        /// 既定構成の適用条件(ActiveLoadout == null)まで潰してしまう。
        /// </para>
        /// </summary>
        private void OnEnable()
        {
            if (slotListView != null) slotListView.SlotSelected += HandleSlotSelected;
            if (partListView != null) partListView.PartChosen += HandlePartChosen;
            if (mechRuntime != null) mechRuntime.LoadoutApplied += HandleLoadoutApplied;

            // 初回の OnEnable では _workingLoadout はまだ無い(Start で作る)。
            // 2回目以降(画面の再表示)ではここで購読を復帰させる。
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

            if (_initialized)
            {
                // 再表示。状態は保持しているので、表示だけ現在値に合わせ直す。
                RefreshSlotList();
                if (_hasSelectedSlot) partListView?.Show(_selectedSlot, partCatalog, _workingLoadout);
            }
        }

        /// <summary>
        /// 状態の初期化はここで行う(D-16)。Start の時点なら同一シーンの全 Awake が終わっている。
        /// </summary>
        private void Start()
        {
            _initialized = true;

            DisablePreviewLocomotion();
            EnsureWorkingLoadout();

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
        /// ガレージのプレビュー機体は「見せるだけ」で、操作させない(CLAUDE.md D-19)。
        /// 有効なままだと戦略が重力を積算して機体が落ちていき、さらに
        /// <see cref="MechPreviewRotator"/> のドラッグ回転と戦略のヨー回転が同じ Transform を奪い合う。
        /// 参照が未配線でも動くよう、MechRuntime と同じ GameObject から拾うフォールバックを持つ。
        /// </summary>
        private void DisablePreviewLocomotion()
        {
            if (previewLocomotion == null && mechRuntime != null)
            {
                previewLocomotion = mechRuntime.GetComponent<MechLocomotionController>();
            }

            if (previewLocomotion != null && previewLocomotion.enabled)
            {
                previewLocomotion.enabled = false;
            }
        }

        /// <summary>
        /// 起動時の作業中 Loadout を決める。優先順位は
        /// <b>保存ファイル → MechRuntime の既定構成 → 空の Loadout</b>(CLAUDE.md D-21)。
        /// 2回目以降は作り直さない(この画面が非表示の間に外部から ActiveLoadout が
        /// 差し替えられるケースまでは v1 では追従しない — 未検証点として報告する)。
        ///
        /// <para>
        /// 保存ファイルを最優先で読むのは、出撃時の自動保存
        /// (<see cref="SceneTransitionButton"/> の saveBeforeLoad)と既定構成での起動が
        /// 組み合わさると、保存済み構成が無言で消えるため:
        /// 構成 A を保存 → 再起動 → ガレージが既定構成で始まる → プレイヤーが「読込」を
        /// 押さずに「テスト走行へ」を押す → 自動保存で A が上書きされる、という順序で
        /// §11-6「構成を保存し、再起動後に復元できる」が最も自然な操作で破れる(D-21)。
        /// <see cref="TestFieldScreen"/> が起動時に同じ保存ファイルを読むのと規則を揃える。
        /// </para>
        /// </summary>
        private void EnsureWorkingLoadout()
        {
            if (_workingLoadout != null)
            {
                return;
            }

            // D-21: まず保存済み構成。読めた場合は既定構成を組ませる必要がない
            // (MechRuntime.Start より先にここが走っても、直後の ApplyWorkingLoadout で
            //  ActiveLoadout がこの構成に確定するため、既定構成に戻されることはない)。
            Loadout restored = TryLoadSavedLoadout();

            if (restored == null)
            {
                // Start の順序は保証されないので、MechRuntime.Start を待たずにこちらから既定構成の
                // 適用を促す。適用済みなら何もしない(D-16)。これが無いと、ガレージが先に走ったときに
                // 空の構成のまま既定機体が永久に使われない。
                if (mechRuntime != null)
                {
                    mechRuntime.EnsureDefaultLoadoutApplied();
                }

                restored = mechRuntime != null && mechRuntime.ActiveLoadout != null
                    ? mechRuntime.ActiveLoadout.Clone()
                    : new Loadout();
            }

            _workingLoadout = restored;
            _workingLoadout.SlotChanged += HandleSlotChanged;
        }

        /// <summary>
        /// 保存ファイルから構成を1件読む。読めなければ null(ファイル無し・破損・カタログ未設定)。
        /// 警告は成否にかかわらず必ず <see cref="ReportDiskResult"/> を通してステータスパネルへ出す
        /// (D-17)。起動時の自動読込(D-21)と「読込」ボタンの両方がこの1経路を通る。
        /// </summary>
        private Loadout TryLoadSavedLoadout()
        {
            if (partCatalog == null)
            {
                return null;
            }

            LoadoutLoadResult result = LoadoutSaveFile.Load(partCatalog);

            // 「保存したはずのパーツが消えた」に気づけるよう、警告はコンソールだけで終わらせず
            // ステータスパネルの警告行にも出す(D-17)。読み込みに失敗した場合も同じ経路で見せる。
            ReportDiskResult(result.Warnings);

            if (!result.Success || result.Loadouts == null || result.Loadouts.Count == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(result.ActiveIndex, 0, result.Loadouts.Count - 1);
            return result.Loadouts[index];
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

        /// <summary>
        /// MechRuntime.LoadoutApplied: ステータスパネルだけを更新する。
        ///
        /// <para>
        /// 作業中 Loadout が未確定のうちは描かない。起動直後、
        /// <see cref="EnsureWorkingLoadout"/> が促す既定構成の適用でこのイベントが先に飛ぶが、
        /// その時点の <see cref="ComputeRawCapabilities"/> は None しか返せず、能力アイコン行が
        /// 一度空で描かれる(同フレーム内の2回目の適用で上書きされるので画面には出ないが、
        /// 無駄な描画である以上に「空 = 能力なし」を正常系の経路で作ってしまう)。
        /// </para>
        /// </summary>
        private void HandleLoadoutApplied()
        {
            if (mechRuntime == null || statPanelView == null || _workingLoadout == null)
            {
                return;
            }

            statPanelView.Refresh(
                mechRuntime.CurrentStats,
                mechRuntime.Validation,
                mechRuntime.Locomotion,
                ComputeRawCapabilities(),
                GetRunSpeedRatio());
        }

        /// <summary>
        /// スプリント倍率の出典は <see cref="MechLocomotionController"/> ただ1つ(D-23)。
        /// ステータスパネルへ副表示する「走行時 ×N」も同じ値を読ませ、ここで別の既定値を
        /// 持たない。参照が取れないときは 0 を返し、パネル側は副表示を出さない
        /// (推測値を出すと D-9 の「表示と実効値が一致する」を静かに破るため)。
        /// </summary>
        private float GetRunSpeedRatio()
        {
            return previewLocomotion != null ? previewLocomotion.RunSpeedRatio : 0f;
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
            ReportDiskResult(result.Warnings);

            if (!result.Success)
            {
                // Warnings にも理由は入っているが、保存の失敗は「気づかないと構成を失う」ので
                // コンソールにも Error として残す。
                Debug.LogError($"[GarageScreen] 構成の保存に失敗しました: {result.Path}", this);
            }
        }

        /// <summary>保存ファイルを読み込み、先頭(activeIndex)の構成を作業中Loadoutとして反映する(M7)。</summary>
        public void LoadFromDisk()
        {
            Loadout loaded = TryLoadSavedLoadout();
            if (loaded == null)
            {
                return;
            }

            if (_workingLoadout != null)
            {
                _workingLoadout.SlotChanged -= HandleSlotChanged;
            }
            _workingLoadout = loaded;
            _workingLoadout.SlotChanged += HandleSlotChanged;

            RefreshSlotList();
            if (_hasSelectedSlot)
            {
                partListView?.Show(_selectedSlot, partCatalog, _workingLoadout);
            }

            // Loadout インスタンスを丸ごと差し替えたので、MechRuntime に明示的に再認識させる。
            ApplyWorkingLoadout();
        }

        /// <summary>
        /// セーブ/ロード経路の警告を、コンソールとステータスパネルの警告行の両方へ流す(D-17)。
        ///
        /// <para>
        /// <see cref="LoadoutSerializer"/> は §7 どおり未知のパーツ ID のスロットを空にして
        /// 警告を積むだけなので、この経路では <see cref="ValidationCode.UnknownPartId"/> の
        /// Error は発火しない(D-13 の Error は実行中のカタログ差し替えに対する防御網)。
        /// つまりここで出さないと、プレイヤーは装備が消えたことに気づけない。
        /// </para>
        /// <para>
        /// 常に呼ぶこと。警告が 0 件のときは空リストを渡してパネル側の古い警告を消す役目も持つ。
        /// 表示は「最後に行ったディスク操作の結果」を意味する。
        /// </para>
        /// </summary>
        private void ReportDiskResult(IReadOnlyList<string> warnings)
        {
            LogWarnings(warnings);
            statPanelView?.SetNotices(warnings);
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
