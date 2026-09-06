using System.Collections.Generic;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// <see cref="LoadoutSerializer.Deserialize"/> / <see cref="LoadoutSaveFile.Load"/> の結果。
    /// 壊れた・古い保存データでも例外を投げず、読める範囲を読み込んだ上で警告を積んで返す
    /// (設計ドキュメント §7 のアセット差し替え耐性)。
    /// </summary>
    public sealed class LoadoutLoadResult
    {
        public LoadoutLoadResult(bool success, List<Loadout> loadouts, int activeIndex, IReadOnlyList<string> warnings)
        {
            Success = success;
            Loadouts = loadouts;
            ActiveIndex = activeIndex;
            Warnings = warnings;
        }

        /// <summary>1件以上のLoadoutを読み込めたか。false でも <see cref="Loadouts"/> は null にはしない。</summary>
        public bool Success { get; }

        public List<Loadout> Loadouts { get; }

        /// <summary>範囲外だった場合は 0 にクランプ済み。</summary>
        public int ActiveIndex { get; }

        /// <summary>日本語の警告メッセージ。空リストの場合もある(null にはしない)。</summary>
        public IReadOnlyList<string> Warnings { get; }
    }
}
