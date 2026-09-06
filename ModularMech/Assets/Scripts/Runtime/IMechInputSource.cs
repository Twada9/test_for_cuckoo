namespace ModularMech.Mechs
{
    /// <summary>
    /// 入力の読み取り口。実装を差し替えられるようにしてあるのは、
    /// EditMode/PlayMode テストとリプレイ(記録した入力の再生)で
    /// <see cref="MechLocomotionController"/> をそのまま動かせるようにするため。
    ///
    /// 実装は毎フレーム呼ばれるので、割り当てを行わないこと。
    /// </summary>
    public interface IMechInputSource
    {
        MechInputState Read();
    }
}
