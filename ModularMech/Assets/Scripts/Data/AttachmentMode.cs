namespace ModularMech.Data
{
    /// <summary>
    /// パーツのメッシュを機体へどう取り付けるか。
    /// 組み立て側(MechAssembly)がこの値だけを見て分岐するため、
    /// パーツ種別ごとの分岐をアセンブリに持ち込まずに済む。
    /// </summary>
    public enum AttachmentMode
    {
        /// <summary>共通スケルトンにウェイト付き(胴・脚など変形するもの)。bones 配列の張り替えが必要。</summary>
        SkinnedToSharedRig,

        /// <summary>ボーンに剛体として親子付け(頭・バックパック・手持ちなど)。</summary>
        RigidToBone,
    }
}
