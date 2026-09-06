namespace ModularMech.Data
{
    /// <summary>パーツ ID から定義を解決する。セーブデータは ID しか持たないため、復元はここを通る。</summary>
    public interface IPartCatalog
    {
        bool TryGet(string partId, out IPartData part);
    }
}
