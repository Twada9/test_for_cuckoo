using ModularMech.Data;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 移動方式1つ分の実装。
    /// 「移動方式の追加でコントローラが肥大化する」を避けるための分離で(設計ドキュメント §10)、
    /// 新方式の追加は **このインターフェースの実装クラス1つ + Registry への登録1行** で完結すること。
    /// <see cref="MechLocomotionController"/> 側に分岐を足したくなったら設計ミスを疑う。
    ///
    /// 実装は毎フレーム <see cref="Tick"/> される。割り当てを行わないこと。
    /// </summary>
    public interface ILocomotionStrategy
    {
        LocomotionType Type { get; }

        /// <summary>この方式に切り替わった直後に1度だけ呼ばれる。速度のリセットなどに使う。</summary>
        void Enter(MechLocomotionContext ctx);

        void Tick(MechLocomotionContext ctx, in MechInputState input, float deltaTime);

        /// <summary>別の方式へ切り替わる / 無効化されるときに呼ばれる。</summary>
        void Exit(MechLocomotionContext ctx);
    }
}
