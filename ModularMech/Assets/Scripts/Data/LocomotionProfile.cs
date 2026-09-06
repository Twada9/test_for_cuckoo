using UnityEngine;

namespace ModularMech.Data
{
    /// <summary>
    /// Legs パーツが持つ移動方式の定義(設計ドキュメント §2.5)。
    /// 「脚を替えると移動そのものが別物になる」の主役であり、
    /// 移動コントローラはこの数値と <see cref="LocomotionType"/> だけを見る。
    /// </summary>
    [CreateAssetMenu(menuName = "Mech/Locomotion Profile", fileName = "Locomotion_")]
    public sealed class LocomotionProfile : ScriptableObject, ILocomotionProfileData
    {
        public LocomotionType type = LocomotionType.Biped;

        public float baseMoveSpeed = 4f;
        public float baseTurnSpeed = 180f;
        public float baseJumpPower = 5f;
        public float acceleration = 20f;

        [Tooltip("接地高さ。ホバー脚は正の値を入れて常時浮かせる。")]
        public float groundOffset;

        [Tooltip("移動方式ごとのクリップ差し替え。ベース AnimatorController の上に被せる。")]
        public AnimatorOverrideController animatorSet;

        [Tooltip("これを超えると過積載。0 以下は「容量未設定」として過積載判定を行わない。")]
        public float weightCapacity = 100f;

        // --- ILocomotionProfileData -------------------------------------------------
        // 純粋ロジック(集約・検証)は ScriptableObject ではなくこの読み取り面越しに触る。

        public LocomotionType Type => type;
        public float BaseMoveSpeed => baseMoveSpeed;
        public float BaseTurnSpeed => baseTurnSpeed;
        public float BaseJumpPower => baseJumpPower;
        public float Acceleration => acceleration;
        public float GroundOffset => groundOffset;
        public float WeightCapacity => weightCapacity;
    }
}
