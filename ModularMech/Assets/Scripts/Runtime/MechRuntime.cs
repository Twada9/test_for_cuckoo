using System;
using ModularMech.Assembling;
using ModularMech.Data;
using ModularMech.Loadouts;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// Loadout を機体の「見た目」と「挙動」へ同時に反映させる集約点(設計ドキュメント §5.1)。
    ///
    /// 流れは一本道:
    ///   解決(LoadoutResolver) → 集約(StatAggregator。ペナルティ込み) → 検証(LoadoutValidator)
    ///   → 組み立て(MechAssembly.Rebuild) → Animator 差し替え → LoadoutApplied 発火
    ///
    /// 脚が無い等で <see cref="Locomotion"/> が null になっても例外は投げない。
    /// 「移動不能な機体」として成立させ、ガレージで組み直せる状態を保つ(設計原則7)。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ModularMech/Mech Runtime")]
    public sealed class MechRuntime : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] MechAssembly assembly;
        [SerializeField] MechAnimationDriver animationDriver;

        [Header("Startup")]
        [SerializeField] PartCatalog catalog;

        [Tooltip("Start で defaultLoadout を適用する。テスト走行シーンで機体を即座に立ち上げるため。")]
        [SerializeField] bool applyOnStart = true;

        [Tooltip("スロットごとの初期パーツ ID。空欄は未装備。")]
        [SerializeField] string[] defaultPartIds = Array.Empty<string>();

        [Header("Behavior")]
        [Tooltip("Loadout.SlotChanged を受けてフレーム末に1回だけ再適用する。")]
        [SerializeField] bool rebuildOnSlotChanged = true;

        [Tooltip("適用のたびに検証結果をコンソールへ出す。UI が出来るまでの確認用。")]
        [SerializeField] bool logValidation = true;

        bool _rebuildRequested;
        bool _applying;

        public StatBlock CurrentStats { get; private set; } = StatBlock.Empty;

        /// <summary>ペナルティ適用後の能力。過積載時は Run が、極端な過積載では Jump も落ちている。</summary>
        public CapabilityFlags Capabilities { get; private set; }

        /// <summary>脚が無い / 脚に移動方式が無いときは null。移動不能を表す正常な状態。</summary>
        public ILocomotionProfileData Locomotion { get; private set; }

        /// <summary>
        /// 最後の適用時に作った検証結果。毎フレーム作り直さない(CLAUDE.md D-10)。
        /// <see cref="LoadoutValidator"/> は List と文字列を新規生成するため、Update から呼ぶと GC を生む。
        /// </summary>
        public LoadoutValidation Validation { get; private set; }

        public Loadout ActiveLoadout { get; private set; }

        /// <summary>最後に解決した構成。UI とアニメーション側が再解決せずに済むよう公開する。</summary>
        public ResolvedLoadout Resolved { get; private set; }

        /// <summary>最後に適用に使ったカタログ。再適用時のフォールバックに使う。</summary>
        public PartCatalog Catalog => catalog;

        /// <summary>脚があり、移動能力(Walk か Hover)が残っているか。</summary>
        public bool CanMove =>
            Locomotion != null &&
            (Capabilities.Has(CapabilityFlags.Walk) || Capabilities.Has(CapabilityFlags.Hover));

        public event Action LoadoutApplied;

        void Awake()
        {
            if (assembly == null)
            {
                assembly = GetComponent<MechAssembly>();
            }

            if (animationDriver == null)
            {
                animationDriver = GetComponent<MechAnimationDriver>();
            }
        }

        void Start()
        {
            if (applyOnStart && ActiveLoadout == null)
            {
                Apply(BuildDefaultLoadout(), catalog);
            }
        }

        void OnDestroy()
        {
            UnsubscribeFromLoadout(ActiveLoadout);
        }

        /// <summary>
        /// 設計ドキュメント §5.1 の集約フロー。
        /// パーツが欠けていても、カタログに無い ID があっても、ここで例外は投げない。
        /// </summary>
        public void Apply(Loadout loadout, PartCatalog partCatalog)
        {
            if (loadout == null)
            {
                Debug.LogWarning("[MechRuntime] Loadout が null。適用しない。", this);
                return;
            }

            PartCatalog effectiveCatalog = partCatalog != null ? partCatalog : catalog;
            if (effectiveCatalog == null)
            {
                Debug.LogWarning("[MechRuntime] PartCatalog が無いため ID を解決できない。適用しない。", this);
                return;
            }

            if (!ReferenceEquals(ActiveLoadout, loadout))
            {
                UnsubscribeFromLoadout(ActiveLoadout);
                ActiveLoadout = loadout;
                SubscribeToLoadout(ActiveLoadout);
            }

            catalog = effectiveCatalog;

            ApplyInternal();
        }

        /// <summary>
        /// 現在の Loadout を再適用する。
        /// 再生中はフレーム末にまとめて1回だけ実行する(D-6)。1操作で SlotChanged が複数回飛ぶため、
        /// そのたびに Apply すると Animator の再代入が重なって T ポーズが見える。
        /// </summary>
        public void RequestRebuild()
        {
            if (ActiveLoadout == null || catalog == null)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                // エディタのプレビューには LateUpdate が回ってこないので、その場で反映する。
                ApplyInternal();
                return;
            }

            _rebuildRequested = true;
        }

        void LateUpdate()
        {
            if (!_rebuildRequested)
            {
                return;
            }

            _rebuildRequested = false;
            ApplyInternal();
        }

        void ApplyInternal()
        {
            if (_applying)
            {
                // 適用中の SlotChanged で再入しない。次フレームに回す。
                _rebuildRequested = true;
                return;
            }

            if (ActiveLoadout == null || catalog == null)
            {
                return;
            }

            _applying = true;
            try
            {
                // 1. 解決: 見つからない ID はここで落ち、MissingPartIds に積まれる。
                Resolved = LoadoutResolver.Resolve(ActiveLoadout, catalog);

                // 2. 集約: 加算合計 → §3.3 のペナルティまで含んだ StatBlock が返る。
                CurrentStats = StatAggregator.Aggregate(Resolved);
                Capabilities = CurrentStats.Capabilities;

                // 3. 検証: ここでしか作らない。以降は Validation を読むだけ(D-10)。
                Validation = LoadoutValidator.Validate(Resolved);

                // 4. 移動方式: 脚が無ければ null のまま。移動不能な機体として成立させる。
                Locomotion = Resolved.Legs != null ? Resolved.Legs.LocomotionProfileData : null;

                // 5. 見た目: 変化したスロットだけ差分リビルド(Legs 変更時のみ全体)。
                if (assembly != null)
                {
                    assembly.Rebuild(ActiveLoadout, catalog);
                }

                // 6. アニメーション: 脚由来の AnimatorOverrideController に差し替える(D-3)。
                if (animationDriver != null)
                {
                    animationDriver.ApplyLoadout(Locomotion);
                }

                if (logValidation)
                {
                    LogValidation();
                }
            }
            finally
            {
                _applying = false;
            }

            // 7. 通知: 移動コントローラと UI はこれを受けて自分の値を作り直す。
            LoadoutApplied?.Invoke();
        }

        /// <summary>Inspector の defaultPartIds からスロット順に Loadout を組む。テスト走行シーンの起動用。</summary>
        public Loadout BuildDefaultLoadout()
        {
            var loadout = new Loadout("Default");
            if (defaultPartIds == null || catalog == null)
            {
                return loadout;
            }

            var slots = PartSlots.All;
            int count = Mathf.Min(slots.Length, defaultPartIds.Length);
            for (int i = 0; i < count; i++)
            {
                string partId = defaultPartIds[i];
                if (string.IsNullOrEmpty(partId))
                {
                    continue;
                }

                if (!catalog.TryGet(partId, out IPartData part))
                {
                    Debug.LogWarning($"[MechRuntime] 既定パーツ '{partId}' がカタログに無い ({slots[i]})。空スロットにする。", this);
                    continue;
                }

                if (!loadout.TryEquip(slots[i], part, out EquipError error))
                {
                    Debug.LogWarning($"[MechRuntime] 既定パーツ '{partId}' を {slots[i]} に装備できない ({error})。", this);
                }
            }

            return loadout;
        }

        void SubscribeToLoadout(Loadout loadout)
        {
            if (loadout == null || !rebuildOnSlotChanged)
            {
                return;
            }

            loadout.SlotChanged += HandleSlotChanged;
        }

        void UnsubscribeFromLoadout(Loadout loadout)
        {
            if (loadout == null)
            {
                return;
            }

            loadout.SlotChanged -= HandleSlotChanged;
        }

        /// <summary>SlotChanged は1操作で複数回飛ぶ。ここでは旗を立てるだけ(D-6)。</summary>
        void HandleSlotChanged(PartSlot slot)
        {
            RequestRebuild();
        }

        void LogValidation()
        {
            if (Validation == null)
            {
                return;
            }

            var issues = Validation.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                if (issue.Severity == ValidationSeverity.Error)
                {
                    Debug.LogError($"[MechRuntime] {issue.Message}", this);
                }
                else
                {
                    Debug.LogWarning($"[MechRuntime] {issue.Message}", this);
                }
            }
        }
    }
}
