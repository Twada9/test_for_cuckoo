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

        // Animator への直接参照は持たない。アニメータへの書き込みは MechAnimationDriver に一本化してある
        // (パラメータ名とダンプ時間をそこだけが知る)。戦略が Animator を直接触れると、
        // 同じパラメータを2箇所から書く経路ができてしまう。
        public MechAnimationDriver AnimationDriver { get; set; }

        // --- Loadout 適用時に焼き込む値 ---------------------------------------------

        /// <summary>脚が無ければ null。その場合、機体は移動不能として扱う。</summary>
        public ILocomotionProfileData Profile { get; set; }

        public CapabilityFlags Capabilities { get; set; }

        /// <summary>
        /// ペナルティ適用後の最大移動速度。<c>StatPanelView</c> が主表示する実効速度と同じ値で、
        /// 戦略はこれに倍率を掛けてはならない(D-14)。
        /// </summary>
        public float MaxMoveSpeed { get; set; }

        /// <summary>
        /// 今フレームの上限速度。<see cref="MaxMoveSpeed"/> にスプリント倍率まで適用済みの
        /// <b>確定値</b>で、<see cref="MechLocomotionController"/> が毎フレーム1回だけ決める(D-23)。
        ///
        /// <para>
        /// 戦略が読むのはこちら。スプリント倍率を戦略ごとにコピーすると、新しい移動方式で
        /// 書き忘れたときに「その方式だけスプリントが効かない」という無反応ができるため、
        /// 掛け算はコントローラに1箇所だけ置く。
        /// </para>
        /// </summary>
        public float EffectiveMoveSpeed { get; set; }

        /// <summary>ペナルティ適用後の旋回速度(度/秒)。</summary>
        public float TurnSpeed { get; set; }

        public float JumpPower { get; set; }

        /// <summary>加速度(LocomotionProfile.Acceleration × パワー不足ペナルティ)。</summary>
        public float Acceleration { get; set; }

        /// <summary>減速度。加速度と別に持つのは、ホバーの「止まらなさ」を表現するため。</summary>
        public float Deceleration { get; set; }

        public float GroundOffset { get; set; }

        // PartStats.stability は v1 の予約フィールド。読む実装を作らない(CLAUDE.md D-4)。

        // スプリント倍率はここに持たない。コントローラが EffectiveMoveSpeed へ畳み込む(D-23)。

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

        /// <summary>
        /// この角度未満の回転は無視する。入力が無いフレームで Transform を書き換えないための足切りで、
        /// 「ゼロと等しいか」の判定ではない(規約: float の等値比較をしない。閾値は &lt; で書く)。
        /// 1フレーム 0.0001 度は 60fps で 1 分回し続けても 0.36 度なので、実質静止と見なしてよい。
        /// </summary>
        const float MinYawDegrees = 0.0001f;

        public void RotateYaw(float degrees)
        {
            if (Transform == null || Mathf.Abs(degrees) < MinYawDegrees)
            {
                return;
            }

            Transform.Rotate(0f, degrees, 0f, Space.Self);
        }

        /// <summary>
        /// 1フレーム分の移動を適用する。CharacterController があればそちらを使い、
        /// 無ければ Transform を直接動かす(ガレージのプレビューなど物理無しの状況向け)。
        ///
        /// <para>
        /// 物理無しの経路では鉛直成分を捨てる。地面が無いので支えようがなく、接地維持のための
        /// 押し付け速度(<see cref="GroundedStickSpeed"/>)の分だけ機体が沈み続けるため。
        /// <see cref="RefreshGrounded"/> が接地扱いにするのと対で、
        /// 「CharacterController が無いときは落下も上昇もしない」に揃える(D-20)。
        /// </para>
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
                worldDelta.y = 0f;
                Transform.position += worldDelta;
            }
        }

        /// <summary>
        /// 目標高度を持つ移動の適用。ホバーが使う(CLAUDE.md D-22)。
        ///
        /// <para>
        /// <see cref="CharacterController"/> があるときは <see cref="ApplyMotion"/> と同じで、
        /// 鉛直も速度として積分する(サスペンションの計算は呼び出し側が済ませている)。
        /// </para>
        /// <para>
        /// 物理が無い(プレビュー)ときは、<see cref="ApplyMotion"/> のように鉛直成分を
        /// 捨てるだけにしてはならない。捨てると <c>GroundOffset</c> まで永久に浮き上がらず、
        /// D-20 が明示的に想定した「物理無しでホバーを見る」用途が成立しないため、
        /// ここでは速度を積分する代わりに <c>Transform.position.y</c> を目標高度へ直接寄せる。
        /// 接地系(<see cref="ApplyMotion"/>)は従来どおり鉛直成分を捨てる ―― 目標高度を持たない
        /// 方式で同じことをすると、支えの無い空間で落下し続けることになるため。
        /// </para>
        /// </summary>
        /// <param name="worldDelta">このフレームの移動量。鉛直成分は物理経路でのみ使う。</param>
        /// <param name="targetY">目標高度(ワールド座標)。</param>
        /// <param name="verticalSpeed">目標高度へ寄せる速さ(m/s)。符号は見ない。</param>
        /// <param name="deltaTime">フレーム時間。</param>
        public void ApplyMotionToAltitude(Vector3 worldDelta, float targetY, float verticalSpeed, float deltaTime)
        {
            if (Controller != null && Controller.enabled)
            {
                ApplyMotion(worldDelta);
                return;
            }

            if (Transform == null)
            {
                return;
            }

            Vector3 position = Transform.position;
            position.x += worldDelta.x;
            position.z += worldDelta.z;
            position.y = Mathf.MoveTowards(position.y, targetY, Mathf.Abs(verticalSpeed) * deltaTime);
            Transform.position = position;

            // 物理が無いときは接地扱いに揃える(D-20)。ホバーはどのみち接地を申告する(D-7)。
            IsGrounded = true;
        }

        /// <summary>
        /// CharacterController から接地状態を取り込む。接地系の戦略だけが使う。
        /// ホバーのように接地しない方式は、これを呼ばずに自分で値を申告する(D-7)。
        ///
        /// <para>
        /// CharacterController が無い(= 物理無しのプレビュー)ときは<b>接地扱い</b>にする
        /// (CLAUDE.md D-20)。<see cref="ApplyMotion"/> がその状況を明示的に想定して
        /// Transform を直接動かす経路を持っているのに、接地判定だけが常に false を返すと、
        /// 戦略が毎フレーム重力を積算し続けて機体が無限に落ちていく。
        /// ホバーが接地を申告するのと同じ「安全側に倒す」判断(D-7)。
        /// </para>
        /// </summary>
        public void RefreshGrounded()
        {
            if (Controller == null || !Controller.enabled)
            {
                IsGrounded = true;
                return;
            }

            IsGrounded = Controller.isGrounded;
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
