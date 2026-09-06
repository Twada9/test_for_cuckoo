using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 装軌(キャタピラ)。直進が速く、旋回がかなり遅い。
    ///
    /// <para>
    /// 「直進が速く旋回が遅い」という数値面の性格は <c>Locomotion_Tracked</c> プロファイルの
    /// baseMoveSpeed / baseTurnSpeed 側に置いてある(CLAUDE.md D-14)。ここに残すのは
    /// 積分の仕方の違いだけで、装軌らしさは次の2点で出す:
    /// </para>
    /// <list type="bullet">
    /// <item>その場旋回: 前後入力が閾値未満なら目標速度を 0 に落とし、履帯を止めてから回る。
    /// 微小な前後入力で這うように進みながら回る、ということが起きない。</item>
    /// <item>横滑りしない: 旋回後、速度ベクトルを新しい正面へ即座に張り直す。
    /// 目標「ベクトル」へ寄せるホバーとは逆の性格になる。</item>
    /// </list>
    /// </summary>
    public sealed class TrackedLocomotionStrategy : ILocomotionStrategy
    {
        /// <summary>この閾値未満の前後入力を「停止中」とみなし、その場旋回に入る。</summary>
        const float PivotForwardThreshold = 0.15f;

        /// <summary>重量物なので加速は鈍い。</summary>
        const float AccelerationScale = 0.7f;

        /// <summary>制動は強い。履帯を止めればすぐ止まる。</summary>
        const float DecelerationScale = 1.8f;

        const float AirControl = 0.2f;

        public LocomotionType Type => LocomotionType.Tracked;

        public void Enter(MechLocomotionContext ctx)
        {
            ctx.ResetMotion();
            ctx.IsHovering = false;
        }

        public void Exit(MechLocomotionContext ctx)
        {
        }

        public void Tick(MechLocomotionContext ctx, in MechInputState input, float deltaTime)
        {
            ctx.RefreshGrounded();

            float forwardInput = Mathf.Clamp(input.move.y, -1f, 1f);
            float turnInput = Mathf.Clamp(input.move.x, -1f, 1f);

            ctx.TurnAmount = turnInput;
            ctx.RotateYaw(turnInput * ctx.TurnSpeed * deltaTime);

            // 履帯は横に滑らない。旋回した分だけ速度ベクトルを新しい正面へ張り直し、
            // 横成分を捨てる。ホバー(目標ベクトルへ寄せる = 横滑りが残る)との対比がここ。
            ctx.PlanarVelocity = ctx.Transform.forward * ctx.ForwardSpeed;

            // その場旋回(信地旋回): 前後入力が閾値未満なら目標速度は 0。
            // 履帯を止めてから回るので、微速前進しながらじりじり回る、ということが起きない。
            bool pivoting = Mathf.Abs(forwardInput) < PivotForwardThreshold;
            float targetSpeed = pivoting ? 0f : forwardInput * ctx.MaxMoveSpeed;
            if (input.sprint)
            {
                targetSpeed *= ctx.RunSpeedRatio;
            }

            Vector3 desiredVelocity = ctx.Transform.forward * targetSpeed;

            bool speedingUp = Mathf.Abs(targetSpeed) > Mathf.Abs(ctx.ForwardSpeed);
            float rate = speedingUp
                ? ctx.Acceleration * AccelerationScale
                : ctx.Deceleration * DecelerationScale;

            if (!ctx.IsGrounded)
            {
                rate *= AirControl;
            }

            ctx.PlanarVelocity = Vector3.MoveTowards(ctx.PlanarVelocity, desiredVelocity, rate * deltaTime);

            if (ctx.IsGrounded)
            {
                if (ctx.VerticalVelocity < 0f)
                {
                    ctx.VerticalVelocity = ctx.GroundedStickSpeed;
                }

                // 装軌脚は通常 Jump 能力を付与しないが、
                // 「能力があるならできる」の原則を崩さないため、方式側では禁止しない。
                if (input.jump && ctx.CanJump)
                {
                    ctx.VerticalVelocity = ctx.JumpPower;
                    ctx.NotifyJumped();
                }
            }

            ctx.VerticalVelocity += ctx.Gravity * deltaTime;

            Vector3 motion = ctx.PlanarVelocity;
            motion.y = ctx.VerticalVelocity;
            ctx.ApplyMotion(motion * deltaTime);

            ctx.UpdateNormalizedSpeed();
        }
    }
}
