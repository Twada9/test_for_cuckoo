namespace ModularMech.Data
{
    /// <summary>脚パーツが決める移動方式。追加時は ILocomotionStrategy の実装を1つ足すだけで済むこと。</summary>
    public enum LocomotionType
    {
        Biped,
        Quadruped,
        Hover,
        Tracked,
    }
}
