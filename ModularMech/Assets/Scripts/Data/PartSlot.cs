namespace ModularMech.Data
{
    /// <summary>機体のパーツスロット。宣言順が UI の表示順を兼ねる。</summary>
    public enum PartSlot
    {
        Head,
        Torso,      // 必須。他スロットの親となる基準
        ArmLeft,
        ArmRight,
        Legs,       // 必須。移動方式を決定する
        Backpack,   // オプション
        HandLeft,   // 手持ち装備(v1では非戦闘オブジェクトのみ)
        HandRight,
    }
}
