using UnityEngine;

namespace ModularMech.Data
{
    /// <summary>
    /// パーツ1種類の定義(設計ドキュメント §2.2)。
    /// 「見た目」と「能力」を同じアセットに持たせることが本作の設計の要で、
    /// 装備した瞬間に両方が同時に変わることを型で保証している。
    /// フィールドは Inspector 露出のため public のまま、ロジック側へはプロパティで見せる。
    /// </summary>
    [CreateAssetMenu(menuName = "Mech/Part Definition", fileName = "Part_")]
    public sealed class PartDefinition : ScriptableObject, IPartData
    {
        [Header("Identity")]
        [Tooltip("一意。セーブデータのキーになるので、公開後は変更しないこと。")]
        public string partId;
        public string displayName;
        [TextArea] public string description;
        public Sprite icon;
        public PartSlot slot;

        [Header("Visual")]
        [Tooltip("SkinnedMeshRenderer または MeshRenderer を持つプレハブ。")]
        public GameObject meshPrefab;
        public AttachmentMode attachmentMode = AttachmentMode.RigidToBone;

        [Header("Stats")]
        public PartStats stats;

        [Header("Behavior")]
        public CapabilityFlags grantedCapabilities;

        [Tooltip("Legs スロットのみ有効。他スロットに入れても無視される。")]
        public LocomotionProfile locomotionProfile;

        [Tooltip("任意。腕パーツなどが上半身レイヤーのクリップを上書きする。")]
        public AnimatorOverrideController animatorOverride;

        // --- IPartData ---------------------------------------------------------------

        public string PartId => partId;
        public PartSlot Slot => slot;
        public PartStats Stats => stats;
        public CapabilityFlags GrantedCapabilities => grantedCapabilities;

        /// <summary>
        /// Legs 以外は null を返す。移動方式は脚だけが決めるという規則を、
        /// 参照側が毎回スロットを確認しなくても守れるようにするため。
        /// </summary>
        public ILocomotionProfileData LocomotionProfileData
        {
            get
            {
                if (slot != PartSlot.Legs) return null;

                // UnityEngine.Object の「破棄済み疑似 null」を本物の null に落として、
                // 純粋ロジック側の `== null` 判定が期待どおり働くようにする。
                return locomotionProfile == null ? null : locomotionProfile;
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(partId) ? $"PartDefinition(<no id>, {slot})" : $"{partId} ({slot})";
        }
    }
}
