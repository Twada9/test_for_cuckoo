namespace ModularMech.Loadouts
{
    /// <summary>検証項目の識別子。UI はメッセージ文字列ではなくこの値で分岐する。</summary>
    public enum ValidationCode
    {
        MissingTorso,
        MissingLegs,
        UnknownPartId,
        SlotMismatch,
        HandWithoutArm,
        PowerBudgetExceeded,
        WeightCapacityExceeded,
    }
}
