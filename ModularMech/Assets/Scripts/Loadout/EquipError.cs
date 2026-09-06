namespace ModularMech.Loadouts
{
    /// <summary>
    /// 装備を拒否した理由。拒否するのは構成として成立しないものだけで、
    /// 過積載・パワー不足は拒否せず警告+ペナルティで許可する(設計ドキュメント §3.2)。
    /// </summary>
    public enum EquipError
    {
        None,

        /// <summary>パーツの想定スロットと装備先スロットが違う。</summary>
        SlotMismatch,

        /// <summary>手持ちスロットに装備しようとしたが、前提となる腕が無い。</summary>
        MissingRequiredArm,

        /// <summary>パーツが null。外す場合は Unequip を使う。</summary>
        NullPart,
    }
}
