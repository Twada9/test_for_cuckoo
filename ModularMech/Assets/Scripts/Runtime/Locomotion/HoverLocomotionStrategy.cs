using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// ホバー。LocomotionProfile.GroundOffset の高さを保って滑るように動く。
    ///
    /// 二脚との差が v1 の成立点なので、次の3点を意図的に強く出す:
    ///  - 接地しない(常に GroundOffset だけ浮く)
    ///  - ジャンプしない。Jump 能力が付いていても踏み切らない(浮いているものは地面を蹴れない)
    ///  - 慣性が強い。入力を切ってもしばらく滑り、旋回しても速度ベクトルが遅れて追従する
    ///
    /// <para>
    /// 「速いが旋回が鈍い」という数値面の性格は <c>Locomotion_Hover</c> プロファイルの
    /// baseMoveSpeed / baseTurnSpeed 側に置いてある(CLAUDE.md D-14)。ここで最高速や
    /// 旋回速度に倍率を掛けると、ステータスパネルの実効速度表示と実挙動が食い違う。
    /// </para>
    /// </summary>
    public sealed class HoverLocomotionStrategy : ILocomotionStrategy
    {
        /// <summary>推力の立ち上がりが鈍い。これが「滑る」感触の本体。</summary>
        const float AccelerationScale = 0.35f;

        /// <summary>減速はさらに鈍い。ブレーキが無いので慣性で流れ続ける。</summary>
        const float DecelerationScale = 0.2f;

        /// <summary>高度誤差 1m あたりの目標上下速度。大きいほど硬いサスペンション。</summary>
        const float HoverStiffness = 6f;

        /// <summary>目標上下速度への追従速度。小さいと段差でふわつく。</summary>
        const float HoverResponse = 24f;

        const float MaxClimbSpeed = 6f;
        const float MaxDescendSpeed = -8f;

        /// <summary>GroundOffset が 0 の脚をホバーに設定した場合でも、地面に擦らない最低高度。</summary>
        const float MinHoverHeight = 0.2f;

        public LocomotionType Type => LocomotionType.Hover;

        public void Enter(MechLocomotionContext ctx)
        {
            ctx.ResetMotion();
            ctx.IsHovering = true;

            // 常に「接地している」と申告する(CLAUDE.md D-7)。
            // 実際には浮いているが、false のままだとベースステートマシンが落下ループに入り、
            // 着地モーションを延々と繰り返す。v1 ではここでステートマシンに嘘をつくのが正解。
            ctx.IsGrounded = true;
        }

        public void Exit(MechLocomotionContext ctx)
        {
            ctx.IsHovering = false;
        }

        public void Tick(MechLocomotionContext ctx, in MechInputState input, float deltaTime)
        {
            // 高度は自前で維持するので CharacterController の接地判定は取り込まない。
            // ただしステートマシンには常に接地を申告する(D-7)。
            ctx.IsGrounded = true;
            ctx.IsHovering = true;

            float turnInput = Mathf.Clamp(input.move.x, -1f, 1f);
            ctx.TurnAmount = turnInput;
            ctx.RotateYaw(turnInput * ctx.TurnSpeed * deltaTime);

            float forwardInput = Mathf.Clamp(input.move.y, -1f, 1f);
            float targetSpeed = forwardInput * ctx.MaxMoveSpeed;
            if (input.sprint)
            {
                targetSpeed *= ctx.RunSpeedRatio;
            }

            Vector3 desiredVelocity = ctx.Transform.forward * targetSpeed;

            bool speedingUp = Mathf.Abs(targetSpeed) > Mathf.Abs(ctx.ForwardSpeed);
            float rate = speedingUp
                ? ctx.Acceleration * AccelerationScale
                : ctx.Deceleration * DecelerationScale;

            // 目標「方向」ではなく目標「ベクトル」へ寄せるので、旋回直後は横滑りが残る。
            ctx.PlanarVelocity = Vector3.MoveTowards(ctx.PlanarVelocity, desiredVelocity, rate * deltaTime);

            if (ctx.TrySampleGroundHeight(out float groundY))
            {
                float desiredY = groundY + Mathf.Max(MinHoverHeight, ctx.GroundOffset);
                float error = desiredY - ctx.Position.y;
                float targetVertical = Mathf.Clamp(error * HoverStiffness, MaxDescendSpeed, MaxClimbSpeed);
                ctx.VerticalVelocity = Mathf.MoveTowards(ctx.VerticalVelocity, targetVertical, HoverResponse * deltaTime);
            }
            else
            {
                // 真下に地面が無い(場外・崖)。支えが無いので普通に落ちる。
                ctx.VerticalVelocity += ctx.Gravity * deltaTime;
            }

            // input.jump は意図的に無視する。ホバーはどの能力構成でも踏み切らない。

            Vector3 motion = ctx.PlanarVelocity;
            motion.y = ctx.VerticalVelocity;
            ctx.ApplyMotion(motion * deltaTime);

            // ApplyMotion が CharacterController.isGrounded を書き戻すので、申告値に戻す(D-7)。
            ctx.IsGrounded = true;

            ctx.UpdateNormalizedSpeed();
        }
    }
}
