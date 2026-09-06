namespace ModularMech.Loadouts
{
    /// <summary>検証結果の重さ。<see cref="ValidationSeverity.Error"/> が1件でもあると出撃できない。</summary>
    public enum ValidationSeverity
    {
        /// <summary>出撃は可能だが性能が落ちる。過積載・パワー不足はこちら(設計ドキュメント §3.2 の設計判断)。</summary>
        Warning,

        /// <summary>構成として成立していない。出撃不可。</summary>
        Error,
    }
}
