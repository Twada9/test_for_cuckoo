using System;
using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using UnityEngine;

namespace ModularMech.Assembling
{
    /// <summary>
    /// Loadout を実体(GameObject)に変換する層。
    ///
    /// 責務は3つだけ:
    ///  1. 前回適用した Loadout との差分を取り、**変化したスロットだけ**破棄・再生成する(§4.2)
    ///  2. パーツを共通スケルトンに接続する(RigidToBone / SkinnedToSharedRig)
    ///  3. 接続に失敗したパーツを安全に捨てて、機体全体を壊さない(§7 / §10)
    ///
    /// ステータスや能力の計算は一切しない。そこは <see cref="ModularMech.Mechs.MechRuntime"/> の仕事。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ModularMech/Mech Assembly")]
    public sealed class MechAssembly : MonoBehaviour
    {
        /// <summary>スロットごとの既定アタッチ先。パーツ側に <see cref="PartAttachment"/> が無いときのフォールバック。</summary>
        [Serializable]
        sealed class SlotBoneBinding
        {
            public PartSlot slot;
            public string boneName;
        }

        [Header("Skeleton")]
        [Tooltip("全パーツが共有するスケルトンのルート。未設定なら自分自身を使う。")]
        [SerializeField] Transform skeletonRoot;

        [Tooltip("ボーン名が決まらない剛体パーツをぶら下げる先。未設定なら skeletonRoot。")]
        [SerializeField] Transform fallbackAttachRoot;

        [Header("Attachment")]
        [SerializeField] SlotBoneBinding[] defaultBoneBindings = Array.Empty<SlotBoneBinding>();

        [Tooltip("生成したパーツ内の Animator を除去する。機体側の Animator と二重に回ると姿勢が競合するため。")]
        [SerializeField] bool stripNestedAnimators = true;

        [Tooltip("再生中でないときに生成物へ DontSave を付け、ガレージのプレビューがシーンに焼き付かないようにする。")]
        [SerializeField] bool markGeneratedAsDontSave = true;

        readonly Dictionary<PartSlot, GameObject> _spawnedParts = new Dictionary<PartSlot, GameObject>(8);

        /// <summary>前回 <see cref="Rebuild"/> に渡された partId。差分判定の基準であり、生成の成否とは独立。</summary>
        readonly Dictionary<PartSlot, string> _appliedPartIds = new Dictionary<PartSlot, string>(8);

        readonly BoneMapper _boneMapper = new BoneMapper();

        public IReadOnlyDictionary<PartSlot, GameObject> SpawnedParts => _spawnedParts;

        public Transform SkeletonRoot => skeletonRoot;

        /// <summary>ボーン辞書。テストやツールから重複ボーン名を検査するために公開する。</summary>
        public BoneMapper Bones => _boneMapper;

        void Awake()
        {
            EnsureBoneMap();
        }

        /// <summary>
        /// 差分リビルド。Legs が変わった場合だけ <see cref="RebuildAll"/> に切り替える
        /// (脚が変わると LocomotionProfile ごと変わり、他パーツの基準姿勢も変わるため。§4.2)。
        /// </summary>
        public void Rebuild(Loadout loadout, PartCatalog catalog)
        {
            if (loadout == null)
            {
                Debug.LogWarning("[MechAssembly] Loadout が null。リビルドを行わない。", this);
                return;
            }

            EnsureBoneMap();

            string nextLegs = loadout.GetPartId(PartSlot.Legs);
            if (!SameId(nextLegs, GetAppliedId(PartSlot.Legs)))
            {
                RebuildAll(loadout, catalog);
                return;
            }

            var slots = PartSlots.All;
            for (int i = 0; i < slots.Length; i++)
            {
                PartSlot slot = slots[i];
                string nextId = loadout.GetPartId(slot);
                if (SameId(nextId, GetAppliedId(slot)))
                {
                    continue;
                }

                DespawnSlot(slot);

                if (!string.IsNullOrEmpty(nextId))
                {
                    SpawnSlot(slot, nextId, catalog);
                }

                // 生成に失敗しても「要求された ID」を記録する。
                // そうしないと同じ失敗を毎リビルド繰り返し、ログを埋め尽くす。
                _appliedPartIds[slot] = nextId;
            }
        }

        /// <summary>全スロットを破棄して作り直す。Legs 変更時と、明示的な初期化で使う。</summary>
        public void RebuildAll(Loadout loadout, PartCatalog catalog)
        {
            Clear();

            if (loadout == null)
            {
                Debug.LogWarning("[MechAssembly] Loadout が null。全体リビルドを行わない。", this);
                return;
            }

            EnsureBoneMap();

            var slots = PartSlots.All;
            for (int i = 0; i < slots.Length; i++)
            {
                PartSlot slot = slots[i];
                string partId = loadout.GetPartId(slot);

                if (!string.IsNullOrEmpty(partId))
                {
                    SpawnSlot(slot, partId, catalog);
                }

                _appliedPartIds[slot] = partId;
            }
        }

        /// <summary>生成済みパーツをすべて破棄し、差分の基準もリセットする。</summary>
        public void Clear()
        {
            var slots = PartSlots.All;
            for (int i = 0; i < slots.Length; i++)
            {
                DespawnSlot(slots[i]);
            }

            _spawnedParts.Clear();
            _appliedPartIds.Clear();
        }

        /// <summary>スケルトンを差し替えたときなど、ボーン辞書を作り直したいときに呼ぶ。</summary>
        public void RebuildBoneMap()
        {
            _boneMapper.Build(ResolveSkeletonRoot());
        }

        void EnsureBoneMap()
        {
            Transform root = ResolveSkeletonRoot();
            if (!_boneMapper.IsBuilt || _boneMapper.Root != root)
            {
                _boneMapper.Build(root);
            }
        }

        Transform ResolveSkeletonRoot()
        {
            if (skeletonRoot == null)
            {
                // 落とさない。自分自身をルートとみなせば、少なくとも階層は成立する。
                skeletonRoot = transform;
                Debug.LogWarning("[MechAssembly] skeletonRoot 未設定のため自分自身を使う。ボーン名解決は失敗しやすい。", this);
            }

            return skeletonRoot;
        }

        void SpawnSlot(PartSlot slot, string partId, PartCatalog catalog)
        {
            if (catalog == null)
            {
                Debug.LogWarning($"[MechAssembly] PartCatalog が null のため '{partId}' ({slot}) を生成できない。", this);
                return;
            }

            // 具象型が要るのは meshPrefab / attachmentMode を見るときだけ。
            // 能力やステータスの解釈は一切ここで行わない。
            if (!catalog.TryGetDefinition(partId, out PartDefinition definition) || definition == null)
            {
                Debug.LogWarning($"[MechAssembly] partId '{partId}' がカタログに無い ({slot})。このスロットは空のままにする。", this);
                return;
            }

            if (definition.Slot != slot)
            {
                // 拒否はバリデータの仕事。ここでは組まないだけに留める。
                Debug.LogWarning($"[MechAssembly] '{partId}' のスロットは {definition.Slot} だが {slot} に指定された。生成しない。", this);
                return;
            }

            if (definition.meshPrefab == null)
            {
                // 見た目を持たないパーツ(内部装備など)は正常系。静かに空スロットとして扱う。
                return;
            }

            GameObject instance = Instantiate(definition.meshPrefab);
            instance.name = $"{slot}_{definition.PartId}";

            bool attached = definition.attachmentMode == AttachmentMode.SkinnedToSharedRig
                ? AttachSkinned(instance, slot)
                : AttachRigid(instance, slot);

            if (!attached)
            {
                // 張り替えに失敗したパーツだけを捨てる。機体全体は生かす(§10 への防御)。
                Debug.LogWarning($"[MechAssembly] '{partId}' ({slot}) をスケルトンに接続できなかったため破棄した。", this);
                DestroySafely(instance);
                return;
            }

            if (stripNestedAnimators)
            {
                StripAnimators(instance);
            }

            if (markGeneratedAsDontSave && !Application.isPlaying)
            {
                ApplyDontSave(instance.transform);
            }

            _spawnedParts[slot] = instance;
        }

        void DespawnSlot(PartSlot slot)
        {
            if (!_spawnedParts.TryGetValue(slot, out GameObject instance))
            {
                return;
            }

            _spawnedParts.Remove(slot);
            DestroySafely(instance);
        }

        /// <summary>
        /// ボーンに剛体として親子付けする。v1 の主役はこちら。
        /// アタッチ先は PartAttachment の boneName、無ければスロット既定ボーン。
        /// </summary>
        bool AttachRigid(GameObject instance, PartSlot slot)
        {
            var attachment = instance.GetComponentInChildren<PartAttachment>(true);

            string boneName = attachment != null ? attachment.BoneName : null;
            if (string.IsNullOrEmpty(boneName))
            {
                boneName = GetDefaultBoneName(slot);
            }

            Transform parent;
            if (string.IsNullOrEmpty(boneName))
            {
                parent = fallbackAttachRoot != null ? fallbackAttachRoot : ResolveSkeletonRoot();
                Debug.LogWarning($"[MechAssembly] {slot} のアタッチ先ボーン名が未設定。'{parent.name}' 直下に付ける。", this);
            }
            else if (!_boneMapper.TryGetBone(boneName, out parent))
            {
                Debug.LogWarning($"[MechAssembly] ボーン '{boneName}' がスケルトンに存在しない ({slot})。", this);
                return false;
            }

            instance.transform.SetParent(parent, false);

            if (attachment != null)
            {
                attachment.ApplyTo(instance.transform);
            }
            else
            {
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }

            return true;
        }

        /// <summary>
        /// SkinnedMeshRenderer の bones / rootBone を共通スケルトンへ張り替える(設計ドキュメント §4.1)。
        /// 1本でも名前解決に失敗したら false を返し、呼び出し側でパーツごと捨てる。
        /// 半分だけ張り替わった状態が見た目としては最悪なので、部分成功は許さない。
        /// </summary>
        bool AttachSkinned(GameObject instance, PartSlot slot)
        {
            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                // Skinned 指定だが実体は剛体メッシュ。作業ミスの可能性が高いので警告し、剛体として救済する。
                Debug.LogWarning($"[MechAssembly] {slot} は SkinnedToSharedRig だが SkinnedMeshRenderer が無い。RigidToBone として扱う。", this);
                return AttachRigid(instance, slot);
            }

            Transform root = ResolveSkeletonRoot();

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer smr = renderers[i];

                if (!_boneMapper.TryResolveBones(smr.bones, out Transform[] newBones, out string missingBone))
                {
                    Debug.LogWarning(
                        $"[MechAssembly] {slot} のボーン '{missingBone}' を共通スケルトンで解決できない。" +
                        "全パーツのボーン名が一致していることが前提(§4.1)。", this);
                    return false;
                }

                smr.bones = newBones;

                Transform sourceRootBone = smr.rootBone;
                if (sourceRootBone != null && _boneMapper.TryGetBone(sourceRootBone.name, out Transform mappedRootBone))
                {
                    smr.rootBone = mappedRootBone;
                }
                else
                {
                    // rootBone はバウンディング計算にしか効かない。ここで捨てるより、
                    // スケルトンルートで代用してカリング精度だけ諦めるほうが安全側。
                    smr.rootBone = root;
                    Debug.LogWarning($"[MechAssembly] {slot} の rootBone を解決できないため skeletonRoot で代用する。", this);
                }
            }

            // スキンメッシュはボーンに従って変形するので、ボーンの兄弟として置く(§4.1)。
            Transform skinParent = root.parent != null ? root.parent : transform;
            instance.transform.SetParent(skinParent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            return true;
        }

        string GetDefaultBoneName(PartSlot slot)
        {
            if (defaultBoneBindings == null)
            {
                return null;
            }

            for (int i = 0; i < defaultBoneBindings.Length; i++)
            {
                SlotBoneBinding binding = defaultBoneBindings[i];
                if (binding != null && binding.slot == slot && !string.IsNullOrEmpty(binding.boneName))
                {
                    return binding.boneName;
                }
            }

            return null;
        }

        string GetAppliedId(PartSlot slot)
        {
            return _appliedPartIds.TryGetValue(slot, out string id) ? id : null;
        }

        static bool SameId(string a, string b)
        {
            // 未装備は null と空文字のどちらも来得るので、まとめて「無し」として扱う。
            bool aEmpty = string.IsNullOrEmpty(a);
            bool bEmpty = string.IsNullOrEmpty(b);
            if (aEmpty || bEmpty)
            {
                return aEmpty && bEmpty;
            }

            return string.Equals(a, b, StringComparison.Ordinal);
        }

        void StripAnimators(GameObject instance)
        {
            var animators = instance.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                DestroySafely(animators[i]);
            }
        }

        static void ApplyDontSave(Transform target)
        {
            target.gameObject.hideFlags = HideFlags.DontSave;
            for (int i = 0; i < target.childCount; i++)
            {
                ApplyDontSave(target.GetChild(i));
            }
        }

        /// <summary>
        /// 再生中は Destroy、エディタ(ガレージのプレビュー)では DestroyImmediate。
        /// エディタで Destroy を呼ぶと次のフレームまで消えず、差分リビルドが二重生成になる。
        /// </summary>
        static void DestroySafely(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
