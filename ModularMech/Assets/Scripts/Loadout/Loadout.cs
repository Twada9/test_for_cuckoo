using System;
using System.Collections.Generic;
using ModularMech.Data;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// スロットごとのパーツ ID の組み合わせ(設計ドキュメント §3.1)。
    /// パーツ定義そのものではなく ID だけを持つのは、保存形式と同じ粒度に揃えて
    /// アセット差し替えへの耐性を持たせるため(§7)。
    /// </summary>
    [Serializable]
    public sealed class Loadout
    {
        private readonly Dictionary<PartSlot, string> _equipped = new Dictionary<PartSlot, string>();

        public Loadout()
        {
        }

        public Loadout(string name)
        {
            Name = name;
        }

        public string Name { get; set; }

        public IReadOnlyDictionary<PartSlot, string> Equipped => _equipped;

        /// <summary>変化したスロットだけを通知する。UI はこれを見て該当行だけ差分更新する。</summary>
        public event Action<PartSlot> SlotChanged;

        /// <summary>未装備なら null。</summary>
        public string GetPartId(PartSlot slot)
        {
            return _equipped.TryGetValue(slot, out var partId) ? partId : null;
        }

        public bool IsEquipped(PartSlot slot)
        {
            return _equipped.ContainsKey(slot);
        }

        /// <summary>
        /// 装備を試みる。拒否するのは <see cref="EquipError.SlotMismatch"/> と
        /// <see cref="EquipError.MissingRequiredArm"/>(と null)のみ。
        /// 重量・電力の超過はここでは見ない。バリデータが警告として扱う。
        /// </summary>
        public bool TryEquip(PartSlot slot, IPartData part, out EquipError error)
        {
            // ID が無いパーツは装備しても保存・復元ができず、
            // 「装備できたのに何も付いていない」状態になるため、null と同じ扱いで弾く。
            if (part == null || string.IsNullOrEmpty(part.PartId))
            {
                error = EquipError.NullPart;
                return false;
            }

            if (part.Slot != slot)
            {
                error = EquipError.SlotMismatch;
                return false;
            }

            // 手持ちは腕にぶら下がる。腕が無いまま手だけ持つ構成は作らせない。
            if (PartSlots.TryGetRequiredArm(slot, out var armSlot) && !_equipped.ContainsKey(armSlot))
            {
                error = EquipError.MissingRequiredArm;
                return false;
            }

            error = EquipError.None;

            var previous = GetPartId(slot);
            _equipped[slot] = part.PartId;

            // 同じパーツを付け直しただけならリビルドも UI 更新も不要。
            if (!string.Equals(previous, part.PartId, StringComparison.Ordinal))
            {
                SlotChanged?.Invoke(slot);
            }

            return true;
        }

        /// <summary>
        /// スロットを空にする。腕を外すと、その腕に依存する手持ちも一緒に外れる
        /// (腕なしで手だけ残ると TryEquip では作れない不正な状態になるため)。
        /// </summary>
        public void Unequip(PartSlot slot)
        {
            if (!_equipped.Remove(slot)) return;

            var handRemoved = false;
            PartSlot handSlot = default;
            if (PartSlots.TryGetDependentHand(slot, out handSlot))
            {
                handRemoved = _equipped.Remove(handSlot);
            }

            // 通知は状態を全て確定させてから出す。購読側が中途半端な構成を読まないようにするため。
            SlotChanged?.Invoke(slot);
            if (handRemoved)
            {
                SlotChanged?.Invoke(handSlot);
            }
        }

        public void Clear()
        {
            if (_equipped.Count == 0) return;

            var removed = new List<PartSlot>(_equipped.Keys);
            _equipped.Clear();

            for (int i = 0; i < removed.Count; i++)
            {
                SlotChanged?.Invoke(removed[i]);
            }
        }

        /// <summary>
        /// 中身を複製する。イベント購読は引き継がない
        /// (コピーは編集中の下書きに使うもので、元の UI を巻き込んではいけないため)。
        /// </summary>
        public Loadout Clone()
        {
            var clone = new Loadout(Name);
            foreach (var pair in _equipped)
            {
                clone._equipped[pair.Key] = pair.Value;
            }
            return clone;
        }

        public LoadoutValidation Validate(IPartCatalog catalog)
        {
            return LoadoutValidator.Validate(this, catalog);
        }
    }
}
