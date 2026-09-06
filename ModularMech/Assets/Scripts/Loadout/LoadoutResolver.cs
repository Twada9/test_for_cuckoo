using System.Collections.Generic;
using ModularMech.Data;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// <see cref="Loadout"/>(ID の集合)と <see cref="IPartCatalog"/> から <see cref="ResolvedLoadout"/> を作る。
    /// </summary>
    public static class LoadoutResolver
    {
        /// <summary>
        /// 解決する。カタログに無い ID は例外にせず未解決として積み、そのスロットは未装備として扱う
        /// (設計ドキュメント §7 のアセット差し替え耐性)。loadout / catalog が null でも空の結果を返す。
        /// </summary>
        public static ResolvedLoadout Resolve(Loadout loadout, IPartCatalog catalog)
        {
            var parts = new Dictionary<PartSlot, IPartData>();
            var missing = new Dictionary<PartSlot, string>();

            if (loadout == null)
            {
                return new ResolvedLoadout(parts, missing);
            }

            for (int i = 0; i < PartSlots.All.Length; i++)
            {
                var slot = PartSlots.All[i];
                var partId = loadout.GetPartId(slot);
                if (string.IsNullOrEmpty(partId)) continue;

                IPartData part = null;
                if (catalog != null && catalog.TryGet(partId, out part) && part != null)
                {
                    parts[slot] = part;
                }
                else
                {
                    // カタログが差し替わった / ID が消えた場合。落とさずに記録だけして先へ進む。
                    missing[slot] = partId;
                }
            }

            return new ResolvedLoadout(parts, missing);
        }
    }
}
