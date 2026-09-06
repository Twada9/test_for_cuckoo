using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 移動戦略が触ってよい可変状態と、機体側の参照をまとめた入れ物。
    ///
    /// 戦略を <see cref="MonoBehaviour"/> にしないための器でもある。戦略は状態を持たず、
    /// このコンテキストだけを読み書きするので、同じ戦略インスタンスを複数機体で共有できる。
    ///
    /// パーツ由来の数値(速度・旋回・加速度・積載ペナルティ)は
    /// <see cref="MechLocomotionController"/> が Loadout 適用時に一度だけ焼き込む。
    /// 毎フレーム <see cref="MechRuntime"/> を辿り直さないのは、Update での参照追跡を避けるため。
    /// </summary>
    public sealed class MechLocomotionContext
    {
        // --- 機体側の参照(Awake でキャッシュ) -------------------------------------

        public Transform Transform { get; set; }
        public CharacterController Controller { get; set; }
        public MechRuntime Runtime { get; set; }
        public Animator Animator { get; set; }
        public MechAnimationDriver AnimationDriver { get; set; }

        // --- Loadout 適用時に焼き込む値 ---------------------------------------------

        /// <summary>脚が無ければ null。その場合、機体は移動不能として扱う。</summary>
        public ILocomotionProfileData Profile { get; set; }

        public CapabilityFlags Capabilities { get; set; }

        /// <summary>ペナルティ適用後の最大移動速度。</summary>
        public float MaxMoveSpeed { get; set; }

        /// <summary>ペナルティ適用後の旋回速度(度/秒)。</summary>
        public float TurnSpeed { get; set; }

        public float JumpPower { get; set; }

        /// <summary>加速度(LocomotionProfile.Acceleration × パワー不足ペナルティ)。</summary>
        public float Acceleration { get; set; }

        /// <summary>減速度。加速度と別に持つのは、ホバーの「止まらなさ」を表現するため。</summary>
        public float Deceleration { get; set; }

        public float GroundOffset { get; set; }

        // PartStats.stability は v1 の予約フィールド。読む実装を作らない(CLAUDE.md D-4)。

        public float RunSpeedRatio { get; set; } = 1.6f;
        public float Gravity { get; set; } = -20f;

        /// <summary>接地時に下向きに与えておく速度。0 だと CharacterController が接地を見失う。</summary>
        public float GroundedStickSpeed { get; set; } = -2f;

        public LayerMask GroundMask { get; set; }
        public float GroundProbeDistance { get; set; } = 20f;

        /// <summary>接地サンプリング用の使い回しバッファ。Update での割り当てを避けるため。</summary>
        readonly RaycastHit[] _groundHits = new RaycastHit[8];

        // --- 毎フレームの可変状態 ---------------------------------------------------

        /// <summary>水平方向の速度(ワールド)。慣性はここに溜まる。</summary>
        public Vector3 PlanarVelocity { get; set; }

        public float VerticalVelocity { get; set; }

        /// <summary>
        /// ステートマシンに申告する接地状態。レイキャストではなく <see cref="ILocomotionStrategy"/> が書く
        /// (CLAUDE.md D-7)。ホバーは常に true を申告し、落下/着地ループに落ちるのを防ぐ。
        /// </summary>
        public bool IsGrounded { get; set; }

        /// <summary>ホバーなど、接地せずに高度を保っている状態。アニメーション側の分岐に使う。</summary>
        public bool IsHovering { get; set; }

        /// <summary>最大速度に対する現在速度の比(0..1)。アニメータの Speed に流す。</summary>
        public float NormalizedSpeed { get; set; }

        /// <summary>今フレームの旋回入力(-1..1)。アニメータの Turn に流す。</summary>
        public float TurnAmount { get; set; }

        /// <summary>今フレームでジャンプが発生したか。コントローラが読んでリセットする。</summary>
        public bool JumpedThisFrame { get; set; }

        public bool CanJump => Capabilities.Has(CapabilityFlags.Jump);

        /// <summary>前進成分の符号付き速度。加速中か減速中かの判定に使う。</summary>
        public float ForwardSpeed => Transform != null ? Vector3.Dot(PlanarVelocity, Transform.forward) : 0f;

        public Vector3 Position => Transform != null ? Transform.position : Vector3.zero;

        /// <summary>速度をゼロに戻す。移動方式の切り替え時に慣性を持ち越さないため。</summary>
        public void ResetMotion()
        {
            PlanarVelocity = Vector3.zero;
            VerticalVelocity = 0f;
            NormalizedSpeed = 0f;
            TurnAmount = 0f;
            JumpedThisFrame = false;
        }

        public void RotateYaw(float degrees)
        {
            if (Transform == null || Mathf.Approximately(degrees, 0f))
            {
                return;
            }

            Transform.Rotate(0f, degrees, 0f, Space.Self);
        }

        /// <summary>
        /// 1フレーム分の移動を適用する。CharacterController があればそちらを使い、
        /// 無ければ Transform を直接動かす(ガレージのプレビューなど物理無しの状況向け)。
        /// </summary>
        public void ApplyMotion(Vector3 worldDelta)
        {
            if (Controller != null && Controller.enabled)
            {
                Controller.Move(worldDelta);
                IsGrounded = Controller.isGrounded;
                return;
            }

            if (Transform != null)
            {
                Transform.position += worldDelta;
            }
        }

        /// <summary>
        /// CharacterController から接地状態を取り込む。接地系の戦略だけが使う。
        /// ホバーのように接地しない方式は、これを呼ばずに自分で値を申告する(D-7)。
        /// </summary>
        public void RefreshGrounded()
        {
            IsGrounded = Controller != null && Controller.enabled && Controller.isGrounded;
        }

        /// <summary>
        /// 真下の地面の高さを取る。ホバーが高度を保つために使う。
        /// RaycastNonAlloc + 使い回しバッファなので、毎フレーム呼んでも割り当てが起きない。
        /// 自機のコライダ(CharacterController のカプセル)は必ず除外する。
        /// レイ始点がカプセル内側になる姿勢があり、そこを拾うと機体が自分の上に浮こうとするため。
        /// </summary>
        public bool TrySampleGroundHeight(out float groundY)
        {
            groundY = 0f;
            if (Transform == null)
            {
                return false;
            }

            // 地面に少しめり込んでいても拾えるよう、足元より上から撃つ。
            Vector3 origin = Transform.position + Vector3.up * 1f;
            int count = Physics.RaycastNonAlloc(
                origin, Vector3.down, _groundHits, GroundProbeDistance, GroundMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = _groundHits[i].transform;
                if (hitTransform == null || hitTransform == Transform || hitTransform.IsChildOf(Transform))
                {
                    continue;
                }

                float distance = _groundHits[i].distance;
                if (distance < nearest)
                {
                    nearest = distance;
                    groundY = _groundHits[i].point.y;
                    found = true;
                }
            }

            return found;
        }

        public void NotifyJumped()
        {
            JumpedThisFrame = true;
        }

        /// <summary>速度から NormalizedSpeed を更新する。最大速度が 0 のときは 0。</summary>
        public void UpdateNormalizedSpeed()
        {
            NormalizedSpeed = MaxMoveSpeed > 0.01f
                ? Mathf.Clamp01(PlanarVelocity.magnitude / MaxMoveSpeed)
                : 0f;
        }
    }
}
