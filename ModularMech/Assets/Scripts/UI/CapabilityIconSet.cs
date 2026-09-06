using System;
using System.Collections.Generic;
using ModularMech.Data;
using UnityEngine;

namespace ModularMech.UI
{
    /// <summary>
    /// CapabilityFlags -> アイコン/表示名の対応表。ScriptableObject にして、
    /// デザイナーがコード変更無しにアイコン・表示名を差し替えられるようにする。
    /// </summary>
    [CreateAssetMenu(menuName = "Mech/UI/Capability Icon Set")]
    public sealed class CapabilityIconSet : ScriptableObject
    {
        [Serializable]
        public struct Mapping
        {
            public CapabilityFlags flag;
            public string displayName;
            public Sprite icon;
        }

        /// <summary>StatPanelView へ渡す表示用の1件。Mapping と同じ形だが読み取り専用として渡す。</summary>
        public readonly struct Entry
        {
            public Entry(CapabilityFlags flag, string displayName, Sprite icon)
            {
                Flag = flag;
                DisplayName = displayName;
                Icon = icon;
            }

            public CapabilityFlags Flag { get; }
            public string DisplayName { get; }
            public Sprite Icon { get; }
        }

        [Tooltip("表示順。登録した順に並ぶ")]
        [SerializeField]
        private List<Mapping> mappings = new List<Mapping>();

        private static readonly IReadOnlyList<Entry> Empty = Array.Empty<Entry>();

        /// <summary>
        /// 登録順に、指定フラグが立っているものだけを返す。未登録のフラグは表示されない。
        /// 呼び出しは装備変更時(LoadoutApplied)のみを想定しており、Update から毎フレーム呼ぶ用途ではない。
        /// </summary>
        public IReadOnlyList<Entry> GetEntries(CapabilityFlags capabilities)
        {
            if (mappings == null || mappings.Count == 0)
            {
                return Empty;
            }

            var result = new List<Entry>(mappings.Count);
            for (int i = 0; i < mappings.Count; i++)
            {
                Mapping mapping = mappings[i];

                // flag 未設定(None)の行を弾く。Has(None) は (value & 0) == 0 で常に true になり、
                // Inspector で追加しただけの空行がどんな構成でも表示されてしまうため。
                if (mapping.flag == CapabilityFlags.None)
                {
                    continue;
                }

                if (capabilities.Has(mapping.flag))
                {
                    result.Add(new Entry(mapping.flag, mapping.displayName, mapping.icon));
                }
            }

            return result;
        }
    }
}
