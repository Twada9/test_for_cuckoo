using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// Application.persistentDataPath 配下への Loadout 保存データの読み書き。
    /// UnityEngine (Application.persistentDataPath) に依存する薄い層で、実際のJSON整形は
    /// UnityEngine非依存の <see cref="LoadoutSerializer"/> に委譲する。
    ///
    /// <para>
    /// 書き込みはテンポラリファイルへ書いてから置き換える方式にし、保存の途中でプロセスが
    /// 落ちても既存の保存ファイルを壊さないようにしている。IO例外は投げずに結果へ警告として積む。
    /// </para>
    /// </summary>
    public static class LoadoutSaveFile
    {
        public const string DefaultFileName = "loadouts.json";

        /// <summary>Application.persistentDataPath 配下の既定の保存先パス。</summary>
        public static string GetDefaultPath()
        {
            return Path.Combine(Application.persistentDataPath, DefaultFileName);
        }

        public static LoadoutSaveResult Save(IReadOnlyList<Loadout> loadouts, int activeIndex, string path = null)
        {
            string targetPath = string.IsNullOrEmpty(path) ? GetDefaultPath() : path;
            var warnings = new List<string>();

            try
            {
                string json = LoadoutSerializer.Serialize(loadouts, activeIndex, pretty: true);

                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tempPath = targetPath + ".tmp";
                // BOM無しUTF-8で書く。JSONはBOMを想定しないパーサが多いため。
                File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                ReplaceAtomically(tempPath, targetPath);

                return new LoadoutSaveResult(true, targetPath, warnings);
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                warnings.Add($"保存に失敗しました: {ex.Message}");
                return new LoadoutSaveResult(false, targetPath, warnings);
            }
        }

        public static LoadoutLoadResult Load(IPartCatalog catalog, string path = null)
        {
            string targetPath = string.IsNullOrEmpty(path) ? GetDefaultPath() : path;

            try
            {
                if (!File.Exists(targetPath))
                {
                    var warnings = new List<string> { "保存ファイルが見つかりません。新規状態として扱います。" };
                    return new LoadoutLoadResult(false, new List<Loadout>(), 0, warnings, fileNotFound: true);
                }

                string json = File.ReadAllText(targetPath, Encoding.UTF8);
                return LoadoutSerializer.Deserialize(json, catalog);
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                var warnings = new List<string> { $"読み込みに失敗しました: {ex.Message}" };
                return new LoadoutLoadResult(false, new List<Loadout>(), 0, warnings);
            }
        }

        /// <summary>
        /// テンポラリファイルを本番パスへ置き換える。File.Replace が使える環境ではそれを使い、
        /// 使えない環境(一部プラットフォームで PlatformNotSupportedException になる)では
        /// delete + move にフォールバックする(その一瞬だけは非原子的になる。下記コメント参照)。
        /// </summary>
        private static void ReplaceAtomically(string tempPath, string targetPath)
        {
            if (!File.Exists(targetPath))
            {
                File.Move(tempPath, targetPath);
                return;
            }

            string backupPath = targetPath + ".bak";
            try
            {
                File.Replace(tempPath, targetPath, backupPath);
                try
                {
                    File.Delete(backupPath);
                }
                catch (IOException)
                {
                    // バックアップの削除に失敗しても本体の置き換えは既に成功しているので致命的ではない。
                }
            }
            catch (PlatformNotSupportedException)
            {
                // File.Replace 未対応の環境向けフォールバック。
                // delete → move の間だけ「保存ファイルが一瞬存在しない」窓ができる点は
                // File.Replace 経路より弱いが、未検証環境での安全策として残す。
                File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
        }

        private static bool IsRecoverableIoException(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Security.SecurityException
                || ex is NotSupportedException
                || ex is ArgumentException;
        }
    }
}
