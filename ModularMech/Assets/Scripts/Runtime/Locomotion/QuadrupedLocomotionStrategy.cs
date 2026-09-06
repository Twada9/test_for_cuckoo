using ModularMech.Data;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 四脚。二脚の派生で「安定寄り」。
    /// 接地面が多い前提なので、最高速と旋回は落ちるが、加速の立ち上がりと制動が良く、
    /// 空中でも姿勢を保てる(AirControl が高い)。跳躍は不得手。
    /// </summary>
    public sealed class QuadrupedLocomotionStrategy : BipedLocomotionStrategy
    {
        public override LocomotionType Type => LocomotionType.Quadruped;

        protected override float SpeedScale => 0.95f;
        protected override float TurnScale => 0.85f;

        /// <summary>脚が4本ある分だけ踏ん張りが利き、指示速度への追従が速い。</summary>
        protected override float AccelerationScale => 1.2f;

        /// <summary>止まりたいときにきちんと止まる。これが「安定寄り」の体感差になる。</summary>
        protected override float DecelerationScale => 1.4f;

        protected override float JumpScale => 0.8f;

        protected override float AirControl => 0.5f;
    }
}
