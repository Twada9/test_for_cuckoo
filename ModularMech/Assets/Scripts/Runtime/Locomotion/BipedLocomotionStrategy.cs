using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 二脚。接地して歩き、重力を受け、Jump 能力があれば跳べる。基準となる移動方式。
    ///
    /// 派生方式(四脚など)が味付けだけを変えられるよう、倍率を protected virtual にしてある。
    /// 数値そのもの(速度・旋回・加速度)は LocomotionProfile とペナルティから来るので、
    /// ここに書く倍率は「方式の性格」だけを表す。
    /// </summary>
    public class BipedLocomotionStrategy : ILocomotionStrategy
    {
        public virtual LocomotionType Type => LocomotionType.Biped;

        protected virtual float SpeedScale => 1f;
        protected virtual float TurnScale => 1f;
        protected virtual float AccelerationScale => 1f;
        protected virtual float DecelerationScale => 1f;
        protected virtual float JumpScale => 1f;

        /// <summary>空中での加減速の効き。1 にすると空中で自由に方向転換できてしまう。</summary>
        protected virtual float AirControl => 0.35f;

        public virtual void Enter(MechLocomotionContext ctx)
        {
            // 方式が変わったら慣性は持ち越さない。ホバーから乗り換えた直後に滑ると挙動差が分かりにくい。
            ctx.ResetMotion();
            ctx.IsHovering = false;
        }

        public virtual void Exit(MechLocomotionContext ctx)
        {
        }

        public virtual void Tick(MechLocomotionContext ctx, in MechInputState input, float deltaTime)
        {
            ctx.RefreshGrounded();

            float turnInput = Mathf.Clamp(input.move.x, -1f, 1f);
            ctx.TurnAmount = turnInput;
            ctx.RotateYaw(turnInput * ctx.TurnSpeed * TurnScale * deltaTime);

            float forwardInput = Mathf.Clamp(input.move.y, -1f, 1f);
            float targetSpeed = forwardInput * ctx.MaxMoveSpeed * SpeedScale;

            // sprint は能力(Run)で既に落とされている前提。ここでは能力を再判定しない。
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
                    // 0 にすると次フレームで接地判定が外れるため、わずかに押し付ける。
                    ctx.VerticalVelocity = ctx.GroundedStickSpeed;
                }

                if (input.jump && ctx.CanJump)
                {
                    ctx.VerticalVelocity = ctx.JumpPower * JumpScale;
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
