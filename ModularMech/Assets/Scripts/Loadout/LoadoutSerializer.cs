using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Serialization;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// Loadout の一覧を設計ドキュメント §7 の JSON 形に厳密一致させて読み書きする。
    /// UnityEngine には依存しない(EditMode テストから直接叩けることが条件)。
    ///
    /// <para>
    /// このクラスは <see cref="Loadout"/> に public な引数無しコンストラクタが存在する前提で書かれている。
    /// CLAUDE.md の共有コントラクトには <c>Loadout()</c> 自体は明記されていないが、
    /// <c>Name</c> が settable なプロパティであること・<c>TryEquip</c> でスロットを後から埋める設計であることから、
    /// 「まず空の Loadout を作って埋める」経路を前提にした。ここが Loadout の実際の実装と食い違う場合は
    /// このファイルの <see cref="ParseLoadout"/> だけを直す。
    /// </para>
    /// </summary>
    public static class LoadoutSerializer
    {
        public const int CurrentVersion = 1;

        private const string KeyVersion = "version";
        private const string KeyLoadouts = "loadouts";
        private const string KeyActiveIndex = "activeLoadoutIndex";
        private const string KeyName = "name";
        private const string KeyParts = "parts";

        /// <summary>
        /// 設計ドキュメント §7 の形で書き出す。未装備スロットは null を書く。
        /// </summary>
        public static string Serialize(IReadOnlyList<Loadout> loadouts, int activeIndex, bool pretty = true)
        {
            var root = new Dictionary<string, object>
            {
                [KeyVersion] = CurrentVersion,
                [KeyLoadouts] = BuildLoadoutsArray(loadouts),
                [KeyActiveIndex] = activeIndex,
            };
            return MiniJson.Serialize(root, pretty);
        }

        private static List<object> BuildLoadoutsArray(IReadOnlyList<Loadout> loadouts)
        {
            var list = new List<object>();
            if (loadouts == null)
            {
                return list;
            }

            foreach (var loadout in loadouts)
            {
                if (loadout == null)
                {
                    // 壊れた要素を書き出すと復元不能になるため、安全側でスキップする。
                    continue;
                }

                var parts = new Dictionary<string, object>();
                foreach (var slot in PartSlots.All)
                {
                    parts[slot.ToString()] = loadout.GetPartId(slot); // 未装備は null
                }

                list.Add(new Dictionary<string, object>
                {
                    [KeyName] = loadout.Name ?? string.Empty,
                    [KeyParts] = parts,
                });
            }

            return list;
        }

        /// <summary>
        /// JSON を読み込む。壊れている・カタログに無い partId がある場合も例外を投げず、
        /// 読める範囲を読み込んで <see cref="LoadoutLoadResult.Warnings"/> に日本語で積む。
        /// </summary>
        public static LoadoutLoadResult Deserialize(string json, IPartCatalog catalog)
        {
            var warnings = new List<string>();

            if (string.IsNullOrEmpty(json))
            {
                warnings.Add("保存データが空です。");
                return new LoadoutLoadResult(false, new List<Loadout>(), 0, warnings);
            }

            object parsed = MiniJson.Deserialize(json);
            if (!(parsed is Dictionary<string, object> root))
            {
                warnings.Add("保存データをJSONとして解析できませんでした。ファイルが壊れている可能性があります。");
                return new LoadoutLoadResult(false, new List<Loadout>(), 0, warnings);
            }

            int version = ReadInt(root, KeyVersion, -1);
            if (version != CurrentVersion)
            {
                // 将来のマイグレーション余地。今は「読めるところまで読む」だけに留める。
                warnings.Add(version < 0
                    ? "保存データに version がありません。現在のバージョンとして読み込みを試みます。"
                    : $"未知の保存バージョン({version})です。互換の範囲で読み込みを試みます。");
            }

            var resultLoadouts = new List<Loadout>();
            if (root.TryGetValue(KeyLoadouts, out object loadoutsObj) && loadoutsObj is List<object> loadoutsList)
            {
                foreach (object entryObj in loadoutsList)
                {
                    if (!(entryObj is Dictionary<string, object> entry))
                    {
                        warnings.Add("形式が不正な構成データを1件スキップしました。");
                        continue;
                    }

                    resultLoadouts.Add(ParseLoadout(entry, catalog, warnings));
                }
            }
            else
            {
                warnings.Add("loadouts が見つからないか形式が不正です。構成なしとして扱います。");
            }

            int activeIndex = ReadInt(root, KeyActiveIndex, 0);
            if (resultLoadouts.Count == 0)
            {
                if (activeIndex != 0)
                {
                    warnings.Add("構成が1件も無いため activeLoadoutIndex を 0 にクランプしました。");
                }
                activeIndex = 0;
            }
            else if (activeIndex < 0 || activeIndex >= resultLoadouts.Count)
            {
                warnings.Add($"activeLoadoutIndex ({activeIndex}) が範囲外のため 0 にクランプしました。");
                activeIndex = 0;
            }

            bool success = resultLoadouts.Count > 0;
            if (!success)
            {
                warnings.Add("読み込める構成が1件もありませんでした。");
            }

            return new LoadoutLoadResult(success, resultLoadouts, activeIndex, warnings);
        }

        private static Loadout ParseLoadout(Dictionary<string, object> entry, IPartCatalog catalog, List<string> warnings)
        {
            var loadout = new Loadout
            {
                Name = ReadString(entry, KeyName, string.Empty),
            };

            if (entry.TryGetValue(KeyParts, out object partsObj) && partsObj is Dictionary<string, object> parts)
            {
                foreach (var slot in PartSlots.All)
                {
                    string key = slot.ToString();
                    if (!parts.TryGetValue(key, out object partIdObj) || partIdObj == null)
                    {
                        continue; // 未装備。スロットは空のまま。
                    }

                    if (!(partIdObj is string partId) || string.IsNullOrEmpty(partId))
                    {
                        warnings.Add($"スロット '{key}' のパーツIDが不正な形式のため、このスロットを空にします。");
                        continue;
                    }

                    if (catalog == null || !catalog.TryGet(partId, out IPartData part))
                    {
                        warnings.Add($"パーツ '{partId}'(スロット '{key}')がカタログに見つかりません。このスロットを空にします。");
                        continue;
                    }

                    if (!loadout.TryEquip(slot, part, out EquipError error))
                    {
                        warnings.Add($"パーツ '{partId}' をスロット '{key}' に装備できませんでした({error})。このスロットを空にします。");
                    }
                }
            }
            else
            {
                warnings.Add($"構成 '{loadout.Name}' に parts が無いか形式が不正です。全スロットを空として扱います。");
            }

            return loadout;
        }

        private static string ReadString(Dictionary<string, object> dict, string key, string fallback)
        {
            if (dict.TryGetValue(key, out object value) && value is string s)
            {
                return s;
            }
            return fallback;
        }

        private static int ReadInt(Dictionary<string, object> dict, string key, int fallback)
        {
            if (dict.TryGetValue(key, out object value) && value is double d)
            {
                return (int)d;
            }
            return fallback;
        }
    }
}
