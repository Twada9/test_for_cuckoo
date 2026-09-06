using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 二脚。接地して歩き、重力を受け、Jump 能力があれば跳べる。基準となる移動方式。
    ///
    /// <para>
    /// 最高速・旋回速度・ジャンプ力に<b>倍率を掛けない</b>(CLAUDE.md D-14)。これらは
    /// <c>StatPanelView</c> がプレイヤーに提示する量であり、戦略側で掛けると表示と実挙動が
    /// 恒久的に食い違うため。方式ごとの速度差・旋回差は <c>LocomotionProfile</c>
    /// (脚アセットごとの数値)だけで表現する。
    /// </para>
    /// <para>
    /// ここに残してよいのは「どう積分するか」の係数だけ ―― 加速・減速の立ち上がりと空中制御。
    /// これらはステータス表示に現れない挙動のパラメータであり、派生方式(四脚など)が
    /// 味付けを変えられるよう protected virtual にしてある。
    /// </para>
    /// </summary>
    public class BipedLocomotionStrategy : ILocomotionStrategy
    {
        public virtual LocomotionType Type => LocomotionType.Biped;

        /// <summary>加速の立ち上がり係数。目標速度そのものには掛からない(D-14)。</summary>
        protected virtual float AccelerationScale => 1f;

        /// <summary>制動の効き係数。目標速度そのものには掛からない(D-14)。</summary>
        protected virtual float DecelerationScale => 1f;

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
            ctx.RotateYaw(turnInput * ctx.TurnSpeed * deltaTime);

            float forwardInput = Mathf.Clamp(input.move.y, -1f, 1f);

            // EffectiveMoveSpeed はスプリント倍率まで適用済みの確定値(D-23)。
            // ここで input.sprint を見て掛け直さないこと。倍率の適用箇所はコントローラ1箇所に限る。
            float targetSpeed = forwardInput * ctx.EffectiveMoveSpeed;

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
                    // JumpPower は LocomotionProfile.BaseJumpPower + jumpPowerMod の値そのもの。
                    // 方式ごとの跳躍差はプロファイル側の数値で表す(D-14)。
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
