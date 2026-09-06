using System.Collections.Generic;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// <see cref="LoadoutSaveFile.Save"/> の結果。CLAUDE.md の共有コントラクトには含まれない、
    /// このファイル(Unity側I/O)専用の小さな戻り値の入れ物。IO例外を握って警告として返すために
    /// <see cref="LoadoutLoadResult"/> と対にして用意した。
    /// </summary>
    public sealed class LoadoutSaveResult
    {
        public LoadoutSaveResult(bool success, string path, IReadOnlyList<string> warnings)
        {
            Success = success;
            Path = path;
            Warnings = warnings;
        }

        public bool Success { get; }
        public string Path { get; }
        public IReadOnlyList<string> Warnings { get; }
    }
}
