using UnityEngine;

namespace ModularMech.Assembling
{
    /// <summary>
    /// パーツプレハブのルートに載せる、アタッチ情報。
    /// 「装備の見た目がめり込む / 浮く」への対策として、
    /// アタッチ先ボーン名と位置/回転/スケールのオフセットをアーティストが Inspector で詰められるようにする
    /// (設計ドキュメント §10)。コード側の定数で調整しないこと。
    ///
    /// このコンポーネントが無いパーツは、<see cref="MechAssembly"/> のスロット既定ボーンに付く。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ModularMech/Part Attachment")]
    public sealed class PartAttachment : MonoBehaviour
    {
        [Header("Attach Target")]
        [Tooltip("親にするボーン名。空ならスロットごとの既定ボーンを使う。RigidToBone のときのみ意味を持つ。")]
        [SerializeField] string boneName = string.Empty;

        [Header("Offsets (めり込み/浮きの微調整用)")]
        [SerializeField] Vector3 positionOffset = Vector3.zero;
        [SerializeField] Vector3 rotationOffsetEuler = Vector3.zero;
        [SerializeField] Vector3 scale = Vector3.one;

        [Tooltip("オフにするとスケールをプレハブのまま残す。等倍でないリグに付けるとき用。")]
        [SerializeField] bool applyScale = true;

        public string BoneName => boneName;
        public Vector3 PositionOffset => positionOffset;
        public Quaternion RotationOffset => Quaternion.Euler(rotationOffsetEuler);
        public Vector3 Scale => scale;
        public bool AppliesScale => applyScale;

        /// <summary>
        /// 親付け(SetParent)を済ませた後に呼び、ローカル TRS をオフセットで上書きする。
        /// SetParent(parent, false) 直後に呼ばれる前提なので、ここでは加算ではなく代入でよい。
        /// </summary>
        public void ApplyTo(Transform target)
        {
            if (target == null)
            {
                return;
            }

            target.localPosition = positionOffset;
            target.localRotation = Quaternion.Euler(rotationOffsetEuler);

            if (applyScale)
            {
                target.localScale = scale;
            }
        }

        /// <summary>エディタで直接値を書き換えるためのセッター(プレースホルダ生成器などから使う)。</summary>
        public void Configure(string bone, Vector3 position, Vector3 eulerRotation, Vector3 localScale)
        {
            boneName = bone;
            positionOffset = position;
            rotationOffsetEuler = eulerRotation;
            scale = localScale;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // スケール 0 は「消えた」と見分けが付かず、原因調査で時間を溶かすので潰しておく。
            if (applyScale && (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f)))
            {
                Debug.LogWarning($"[PartAttachment] '{name}' のスケールに 0 が含まれている。", this);
            }
        }
#endif
    }
}
