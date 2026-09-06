using System.Collections.Generic;
using UnityEngine;

namespace ModularMech.Data
{
    /// <summary>
    /// パーツ定義の一覧と、ID からの解決を担う(設計ドキュメント §7)。
    /// セーブデータはパーツ ID しか持たないため、復元は必ずここを通る。
    /// 索引は遅延構築し、Inspector で中身を編集したら破棄して作り直す。
    /// </summary>
    [CreateAssetMenu(menuName = "Mech/Part Catalog", fileName = "PartCatalog")]
    public sealed class PartCatalog : ScriptableObject, IPartCatalog
    {
        private static readonly PartDefinition[] EmptyParts = new PartDefinition[0];

        [SerializeField]
        private List<PartDefinition> parts = new List<PartDefinition>();

        // 索引。null は「未構築」を意味する。
        private Dictionary<string, PartDefinition> _byId;
        private Dictionary<PartSlot, List<PartDefinition>> _bySlot;
        private List<PartDefinition> _validParts;

        /// <summary>null 要素・空 ID・重複 ID を取り除いた、実際に使えるパーツ一覧。</summary>
        public IReadOnlyList<PartDefinition> AllParts
        {
            get
            {
                EnsureIndex();
                return _validParts;
            }
        }

        /// <summary>指定スロットに装備できるパーツ一覧。該当なしなら空(null を返さない)。</summary>
        public IReadOnlyList<PartDefinition> PartsForSlot(PartSlot slot)
        {
            EnsureIndex();
            return _bySlot.TryGetValue(slot, out var list) ? (IReadOnlyList<PartDefinition>)list : EmptyParts;
        }

        /// <summary>
        /// ID からパーツを解決する。見つからなくても例外は投げない。
        /// 呼び出し側(LoadoutResolver)が「そのスロットは未装備」として安全側に倒す。
        /// </summary>
        public bool TryGet(string partId, out IPartData part)
        {
            if (TryGetDefinition(partId, out var definition))
            {
                part = definition;
                return true;
            }

            part = null;
            return false;
        }

        /// <summary>
        /// <see cref="TryGet"/> の ScriptableObject 版。
        /// 組み立て層は meshPrefab など IPartData に無い情報を必要とするため、キャストを強いないよう用意している。
        /// </summary>
        public bool TryGetDefinition(string partId, out PartDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(partId)) return false;

            EnsureIndex();
            if (!_byId.TryGetValue(partId, out var found) || found == null) return false;

            definition = found;
            return true;
        }

        /// <summary>
        /// 登録内容を丸ごと差し替える。プレースホルダ生成などのエディタスクリプトから使う想定で、
        /// SerializedObject 経由でプライベートフィールド名に依存させないために用意している。
        /// </summary>
        public void SetParts(IReadOnlyList<PartDefinition> newParts)
        {
            if (parts == null) parts = new List<PartDefinition>();
            parts.Clear();

            if (newParts != null)
            {
                for (int i = 0; i < newParts.Count; i++)
                {
                    parts.Add(newParts[i]);
                }
            }

            Invalidate();
        }

        /// <summary>索引を破棄する。実行中にリストを差し替えた場合に呼ぶ。</summary>
        public void Invalidate()
        {
            _byId = null;
            _bySlot = null;
            _validParts = null;
        }

        private void OnEnable()
        {
            // ドメインリロード後に古い索引を持ち越さない。
            Invalidate();
        }

        private void OnValidate()
        {
            // Inspector でリストを編集したら次のアクセスで作り直す。
            Invalidate();
        }

        private void EnsureIndex()
        {
            if (_byId != null) return;

            _byId = new Dictionary<string, PartDefinition>();
            _bySlot = new Dictionary<PartSlot, List<PartDefinition>>();
            _validParts = new List<PartDefinition>();

            if (parts == null) return;

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];

                // 不正な登録は落とさず、原因が分かる形で報告して読み飛ばす(設計ドキュメント §7)。
                if (part == null)
                {
                    Debug.LogError($"[PartCatalog] '{name}' の要素 {i} が null です。カタログから除外します。", this);
                    continue;
                }

                if (string.IsNullOrEmpty(part.partId))
                {
                    Debug.LogError($"[PartCatalog] '{name}' の要素 {i} ({part.name}) に partId がありません。カタログから除外します。", this);
                    continue;
                }

                if (_byId.TryGetValue(part.partId, out var existing))
                {
                    Debug.LogError(
                        $"[PartCatalog] '{name}' で partId「{part.partId}」が重複しています " +
                        $"({existing.name} と {part.name})。先に登録された方を使います。", this);
                    continue;
                }

                _byId.Add(part.partId, part);
                _validParts.Add(part);

                if (!_bySlot.TryGetValue(part.slot, out var slotList))
                {
                    slotList = new List<PartDefinition>();
                    _bySlot.Add(part.slot, slotList);
                }
                slotList.Add(part);
            }
        }
    }
}
