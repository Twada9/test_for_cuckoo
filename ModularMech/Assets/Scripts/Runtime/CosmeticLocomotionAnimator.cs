using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// RigidToBone で取り付けた装飾用モデル(VRM 等)向けの補助コンポーネント。
    ///
    /// <para>
    /// このモデルは機体本体とは別の独立したスケルトンを持つため、本体の
    /// <see cref="MechAnimationDriver"/> / 共有 Animator からは腕や脚を動かせない(D-1)。
    /// このコンポーネントは、モデル自身が持つ Animator に対して「本体がどれだけ速く
    /// 動いているか」だけを毎フレーム橋渡しする。本体の内部実装(MechRuntime /
    /// MechLocomotionController)には一切依存せず、指定した Transform の移動量から
    /// 速度を自前で計算するので、パーツを外しても本体側に影響しない。
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("ModularMech/Cosmetic Locomotion Animator")]
    public sealed class CosmeticLocomotionAnimator : MonoBehaviour
    {
        [Tooltip("速度計測の基準にする Transform。未設定ならこのオブジェクトの最上位の親(機体ルート)を使う。")]
        [SerializeField] private Transform speedReference;

        [Tooltip("Animator に渡す float パラメータ名。VRM 側の Animator Controller で用意しておくこと。")]
        [SerializeField] private string speedParameter = "Speed";

        [Tooltip("速度の変化にどれだけ速く追従するか。大きいほど反応が早く、小さいほど滑らかになる。")]
        [SerializeField] private float smoothing = 12f;

        private Animator _animator;
        private Vector3 _lastPosition;
        private float _smoothedSpeed;
        private int _speedHash;
        private bool _hasLastPosition;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _speedHash = Animator.StringToHash(speedParameter);
            if (speedReference == null)
            {
                speedReference = transform.root;
            }
        }

        private void OnEnable()
        {
            // 有効化された瞬間の移動量を「速度」として誤検出しないよう、次のフレームから測り直す。
            _hasLastPosition = false;
        }

        private void Update()
        {
            if (speedReference == null || _animator == null)
            {
                return;
            }

            Vector3 position = speedReference.position;
            if (!_hasLastPosition)
            {
                _lastPosition = position;
                _hasLastPosition = true;
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            float instantSpeed = (position - _lastPosition).magnitude / deltaTime;
            _lastPosition = position;

            // 指数移動平均で滑らかにする。stability 等のステータスには依存しない、純粋に見た目のための平滑化。
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instantSpeed, 1f - Mathf.Exp(-smoothing * deltaTime));
            _animator.SetFloat(_speedHash, _smoothedSpeed);
        }
    }
}
