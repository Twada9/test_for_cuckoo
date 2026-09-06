using System.Collections.Generic;
using UnityEngine;

namespace ModularMech.Assembling
{
    /// <summary>
    /// スケルトンルート以下を **1度だけ** 走査して「ボーン名 -> Transform」を作る。
    /// 換装のたびに階層を掘り直すと、パーツ数 × ボーン数の探索が毎回走って重くなるため、
    /// 引き当ては必ずこの辞書経由で行う(設計ドキュメント §4.1)。
    ///
    /// 名前解決の失敗は例外にしない。呼び出し側(<see cref="MechAssembly"/>)が
    /// 「そのパーツだけ捨てる」判断をできるよう、必ず bool で返す(§7 / §10)。
    /// </summary>
    public sealed class BoneMapper
    {
        // ボーン名は重複し得る(左右で同名など)。先勝ちで保持し、重複は警告に回す。
        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>(64);
        readonly List<string> _duplicateNames = new List<string>(4);
        readonly Stack<Transform> _walkStack = new Stack<Transform>(64);

        Transform _root;

        public BoneMapper()
        {
        }

        public BoneMapper(Transform skeletonRoot)
        {
            Build(skeletonRoot);
        }

        /// <summary>走査済みのスケルトンルート。未構築なら null。</summary>
        public Transform Root => _root;

        public int BoneCount => _bones.Count;

        /// <summary>重複していたボーン名。空でなければリグが換装向きでない(§10 の最大の落とし穴)。</summary>
        public IReadOnlyList<string> DuplicateNames => _duplicateNames;

        public bool IsBuilt => _root != null && _bones.Count > 0;

        public IReadOnlyDictionary<string, Transform> Bones => _bones;

        /// <summary>
        /// スケルトンを走査して辞書を作り直す。ルートが同じでも階層が変わり得るので、
        /// 呼び出し側が必要と判断したときだけ呼ぶ。
        /// </summary>
        public void Build(Transform skeletonRoot)
        {
            Clear();

            if (skeletonRoot == null)
            {
                Debug.LogWarning("[BoneMapper] skeletonRoot が null のため、ボーン辞書を構築できない。");
                return;
            }

            _root = skeletonRoot;

            // GetComponentsInChildren は配列を確保するので、明示スタックで1度だけ深さ優先に走る。
            _walkStack.Push(skeletonRoot);
            while (_walkStack.Count > 0)
            {
                Transform current = _walkStack.Pop();
                Register(current);

                for (int i = current.childCount - 1; i >= 0; i--)
                {
                    _walkStack.Push(current.GetChild(i));
                }
            }

            if (_duplicateNames.Count > 0)
            {
                Debug.LogWarning(
                    $"[BoneMapper] '{skeletonRoot.name}' 以下でボーン名が重複している ({_duplicateNames.Count} 件): " +
                    $"{string.Join(", ", _duplicateNames)}。" +
                    "名前解決は最初に見つかった Transform を採用するため、パーツが意図しないボーンに付く可能性がある。",
                    skeletonRoot);
            }
        }

        public void Clear()
        {
            _bones.Clear();
            _duplicateNames.Clear();
            _walkStack.Clear();
            _root = null;
        }

        /// <summary>ボーン名で引く。見つからなくても例外にしない。</summary>
        public bool TryGetBone(string boneName, out Transform bone)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                bone = null;
                return false;
            }

            return _bones.TryGetValue(boneName, out bone);
        }

        /// <summary>
        /// SkinnedMeshRenderer の bones 配列を、共通スケルトン側の Transform に読み替える。
        ///
        /// 解決できなかったボーンは **元の Transform をそのまま残す**(CLAUDE.md D-2)。
        /// 1本の名前違いで機体が丸ごと消えるより、その1本だけスキンが追従しないほうが被害が小さく、
        /// 原因も見た目から特定しやすいため。何本落ちたかは戻り値で受け取り、警告に使う。
        /// </summary>
        /// <returns>解決できなかったボーンの数。0 なら完全一致。</returns>
        public int ResolveBonesLenient(Transform[] source, out Transform[] resolved, out string firstMissingBoneName)
        {
            firstMissingBoneName = null;

            if (source == null)
            {
                resolved = null;
                return 0;
            }

            var result = new Transform[source.Length];
            int unresolved = 0;

            for (int i = 0; i < source.Length; i++)
            {
                Transform sourceBone = source[i];
                if (sourceBone != null && _bones.TryGetValue(sourceBone.name, out Transform target))
                {
                    result[i] = target;
                    continue;
                }

                // 直接インデックス (boneMap[name]) は使わない。ここが §10 で最も壊れやすい箇所。
                result[i] = sourceBone;
                unresolved++;

                if (firstMissingBoneName == null)
                {
                    firstMissingBoneName = sourceBone != null ? sourceBone.name : $"(bones[{i}] が null)";
                }
            }

            resolved = result;
            return unresolved;
        }

        void Register(Transform bone)
        {
            string name = bone.name;
            if (_bones.ContainsKey(name))
            {
                if (!_duplicateNames.Contains(name))
                {
                    _duplicateNames.Add(name);
                }

                return; // 先勝ち。後から来た同名ボーンは無視する。
            }

            _bones.Add(name, bone);
        }
    }
}
