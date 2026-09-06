namespace ModularMech.Data
{
    /// <summary>
    /// <see cref="PartSlot"/> にまつわるスロット間のルール。
    /// 「どのスロットが必須か」「手はどの腕に依存するか」を一箇所に閉じ込め、
    /// バリデータ・UI・組み立てが同じ規則を参照できるようにする。
    /// </summary>
    public static class PartSlots
    {
        /// <summary>宣言順の全スロット。毎フレーム列挙しても割り当てが起きないよう配列で保持する。</summary>
        public static readonly PartSlot[] All =
        {
            PartSlot.Head,
            PartSlot.Torso,
            PartSlot.ArmLeft,
            PartSlot.ArmRight,
            PartSlot.Legs,
            PartSlot.Backpack,
            PartSlot.HandLeft,
            PartSlot.HandRight,
        };

        /// <summary>欠けていると不正な Loadout になるスロット。</summary>
        public static bool IsRequired(PartSlot slot)
        {
            return slot == PartSlot.Torso || slot == PartSlot.Legs;
        }

        /// <summary>
        /// 手持ちスロットが前提とする腕スロットを返す。手持ち以外なら false。
        /// </summary>
        public static bool TryGetRequiredArm(PartSlot handSlot, out PartSlot armSlot)
        {
            switch (handSlot)
            {
                case PartSlot.HandLeft:
                    armSlot = PartSlot.ArmLeft;
                    return true;
                case PartSlot.HandRight:
                    armSlot = PartSlot.ArmRight;
                    return true;
                default:
                    armSlot = default;
                    return false;
            }
        }

        /// <summary>腕スロットが支える手持ちスロットを返す。腕以外なら false。</summary>
        public static bool TryGetDependentHand(PartSlot armSlot, out PartSlot handSlot)
        {
            switch (armSlot)
            {
                case PartSlot.ArmLeft:
                    handSlot = PartSlot.HandLeft;
                    return true;
                case PartSlot.ArmRight:
                    handSlot = PartSlot.HandRight;
                    return true;
                default:
                    handSlot = default;
                    return false;
            }
        }
    }
}
