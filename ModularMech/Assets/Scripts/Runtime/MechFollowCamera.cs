using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// テスト走行画面向けの簡易三人称追従カメラ(設計ドキュメント §6.2)。
    ///
    /// 移動そのものには一切関知しない、見るだけの補助コンポーネント。
    /// <see cref="MechLocomotionController"/> がどの <c>ILocomotionStrategy</c> を使っていても、
    /// このカメラは対象の Transform を追うだけなので変更不要(設計原則2と同じ考え方: 見る側も
    /// パーツ種別 / 移動方式を意識しない)。
    ///
    /// Update 内での GetComponent / 新規アロケーションを避けるため、参照は Inspector で結線し、
    /// LateUpdate では SmoothDamp 用の速度バッファ以外を割り当てない。
    /// </summary>
    [AddComponentMenu("ModularMech/Mech Follow Camera")]
    public sealed class MechFollowCamera : MonoBehaviour
    {
        [Tooltip("追従対象。機体のルート Transform。")]
        [SerializeField] private Transform target;

        [Tooltip("対象のローカル空間で見た、カメラの相対位置(後方・上方が基本)。")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 3.5f, -6f);

        [SerializeField] private float positionSmoothTime = 0.15f;
        [SerializeField] private float rotationSpeed = 6f;
        [Tooltip("注視点の高さオフセット(機体の足元ではなく胴体あたりを見る)。")]
        [SerializeField] private float lookHeight = 1.2f;

        private Vector3 _velocity;

        /// <summary>Editor スクリプト / 他コードから追従対象を差し替える。</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desiredPosition = target.position + target.TransformDirection(offset);
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _velocity, positionSmoothTime);

            Vector3 lookPoint = target.position + Vector3.up * lookHeight;
            Vector3 lookDirection = lookPoint - transform.position;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSpeed * Time.deltaTime);
        }
    }
}
