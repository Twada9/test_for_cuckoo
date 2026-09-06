using ModularMech.Data;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 四脚。二脚の派生で「安定寄り」。
    ///
    /// <para>
    /// 「少し遅く、旋回も鈍く、跳べない」という性格は <c>Locomotion_Quadruped</c> プロファイルの
    /// 数値(baseMoveSpeed / baseTurnSpeed / baseJumpPower)側に置いてある(CLAUDE.md D-14)。
    /// ここに残すのは積分の仕方の違いだけ ―― 接地面が多い分だけ踏ん張りが利き、
    /// 指示速度への追従と制動が速く、空中でも姿勢を保てる。
    /// </para>
    /// </summary>
    public sealed class QuadrupedLocomotionStrategy : BipedLocomotionStrategy
    {
        public override LocomotionType Type => LocomotionType.Quadruped;

        /// <summary>脚が4本ある分だけ踏ん張りが利き、指示速度への追従が速い。</summary>
        protected override float AccelerationScale => 1.2f;

        /// <summary>止まりたいときにきちんと止まる。これが「安定寄り」の体感差になる。</summary>
        protected override float DecelerationScale => 1.4f;

        protected override float AirControl => 0.5f;
    }
}
