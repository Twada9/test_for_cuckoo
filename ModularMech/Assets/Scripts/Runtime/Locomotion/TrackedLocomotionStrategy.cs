using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 装軌(キャタピラ)。直進が速く、旋回が遅い。
    /// 前進していなくても左右入力だけでその場旋回(信地旋回)ができ、
    /// 停止時のほうが旋回が速い。二脚との差はここと最高速に出る。
    /// </summary>
    public sealed class TrackedLocomotionStrategy : ILocomotionStrategy
    {
        /// <summary>直進は速い。</summary>
        const float SpeedScale = 1.25f;

        /// <summary>旋回は遅い。装軌の性格そのもの。</summary>
        const float TurnScale = 0.45f;

        /// <summary>その場旋回のときだけ旋回が速くなる(履帯を逆回転させる分)。</summary>
        const float PivotTurnBonus = 1.8f;

        /// <summary>この閾値未満の前後入力を「停止中」とみなす。</summary>
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

            float turnScale = TurnScale;
            if (Mathf.Abs(forwardInput) < PivotForwardThreshold)
            {
                turnScale *= PivotTurnBonus;
            }

            ctx.TurnAmount = turnInput;
            ctx.RotateYaw(turnInput * ctx.TurnSpeed * turnScale * deltaTime);

            float targetSpeed = forwardInput * ctx.MaxMoveSpeed * SpeedScale;
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
