using System.Collections.Generic;
using ModularMech.Data;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// <see cref="Loadout"/> の ID をカタログで実体に解決した結果。
    /// 集約・検証はこの型だけを見ればよく、カタログや ScriptableObject には触れない。
    /// 解決できなかった ID は <see cref="MissingPartIds"/> に積まれ、そのスロットは未装備として扱われる。
    /// </summary>
    public sealed class ResolvedLoadout
    {
        private static readonly Dictionary<PartSlot, IPartData> EmptyParts = new Dictionary<PartSlot, IPartData>();
        private static readonly Dictionary<PartSlot, string> EmptyMissing = new Dictionary<PartSlot, string>();

        private readonly Dictionary<PartSlot, IPartData> _parts;
        private readonly Dictionary<PartSlot, string> _missingBySlot;
        private readonly List<string> _missingPartIds;

        /// <param name="parts">解決できたスロットとパーツ。</param>
        /// <param name="missingBySlot">カタログに無かったスロットとその ID。</param>
        public ResolvedLoadout(Dictionary<PartSlot, IPartData> parts, Dictionary<PartSlot, string> missingBySlot)
        {
            _parts = parts ?? EmptyParts;
            _missingBySlot = missingBySlot ?? EmptyMissing;

            // 辞書の列挙順に依存しないよう、スロット宣言順で ID を並べる。
            _missingPartIds = new List<string>(_missingBySlot.Count);
            if (_missingBySlot.Count > 0)
            {
                for (int i = 0; i < PartSlots.All.Length; i++)
                {
                    if (_missingBySlot.TryGetValue(PartSlots.All[i], out var partId))
                    {
                        _missingPartIds.Add(partId);
                    }
                }
            }

            Torso = GetOrNull(PartSlot.Torso);
            Legs = GetOrNull(PartSlot.Legs);
        }

        public IReadOnlyDictionary<PartSlot, IPartData> Parts => _parts;

        /// <summary>カタログに無かったパーツ ID(スロット宣言順)。同じ ID が複数スロットにあれば重複して入る。</summary>
        public IReadOnlyList<string> MissingPartIds => _missingPartIds;

        /// <summary>カタログに無かった ID を、どのスロットのものだったか付きで返す。UI の該当スロット強調に使う。</summary>
        public IReadOnlyDictionary<PartSlot, string> MissingBySlot => _missingBySlot;

        /// <summary>必須スロット。未装備なら null。</summary>
        public IPartData Torso { get; }

        /// <summary>必須スロット。未装備なら null。移動方式はここからしか取れない。</summary>
        public IPartData Legs { get; }

        /// <summary>脚が無い、または脚に移動方式が設定されていなければ null。</summary>
        public ILocomotionProfileData Locomotion => Legs?.LocomotionProfileData;

        public bool HasMissingParts => _missingPartIds.Count > 0;

        public bool TryGet(PartSlot slot, out IPartData part)
        {
            return _parts.TryGetValue(slot, out part);
        }

        /// <summary>装備されているパーツ全て。</summary>
        public IEnumerable<IPartData> All => _parts.Values;

        private IPartData GetOrNull(PartSlot slot)
        {
            return _parts.TryGetValue(slot, out var part) ? part : null;
        }
    }
}
