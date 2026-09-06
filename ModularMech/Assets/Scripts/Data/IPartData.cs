namespace ModularMech.Data
{
    /// <summary>
    /// 集約・検証・シリアライズが参照するパーツの読み取り面。
    /// ScriptableObject である <see cref="PartDefinition"/> に直接依存させないことで、
    /// 純粋ロジックを UnityEngine 抜きでテストできるようにしている。
    /// </summary>
    public interface IPartData
    {
        string PartId { get; }
        PartSlot Slot { get; }
        PartStats Stats { get; }
        CapabilityFlags GrantedCapabilities { get; }

        /// <summary>Legs スロット以外は null。</summary>
        ILocomotionProfileData LocomotionProfileData { get; }
    }
}
