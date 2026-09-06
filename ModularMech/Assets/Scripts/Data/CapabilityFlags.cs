using System;

namespace ModularMech.Data
{
    /// <summary>
    /// パーツが「何をできるようにするか」。挙動の分岐は原則このフラグだけを見る。
    /// コントローラがパーツの種類を知らずに済むのは、この間接層があるため。
    /// </summary>
    [Flags]
    public enum CapabilityFlags
    {
        None      = 0,
        Walk      = 1 << 0,
        Run       = 1 << 1,
        Jump      = 1 << 2,
        Hover     = 1 << 3,   // Backpack / Legs が付与
        Dash      = 1 << 4,
        Crouch    = 1 << 5,
        GrabLeft  = 1 << 6,   // ArmLeft がないと手持ちできない
        GrabRight = 1 << 7,
        Emote     = 1 << 8,
    }
}
