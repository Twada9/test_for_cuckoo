using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using ModularMech.Mechs;
using UnityEngine;

namespace ModularMech.UI
{
    /// <summary>
    /// テスト走行画面の起点。ガレージで組んだ構成をここでも試せるよう、起動時に保存ファイルが
    /// あれば読み込んで機体へ適用する(設計ドキュメント §6.2「組んだ機体を実際に動かして挙動差を
    /// 体感する」の前提)。
    ///
    /// <para>
    /// <see cref="MechRuntime.Apply"/> は Inspector の defaultPartIds から既定機体を組み立てる
    /// (安全側フォールバック、CLAUDE.md 設計原則7)。だがこれはシーンをまたいで Loadout を
    /// 引き継ぐ経路ではない――Garage と TestField は別シーンで、別インスタンスの MechRuntime を
    /// 持つため。ここでは <see cref="ModularMech.Loadouts.LoadoutSaveFile"/> 経由の保存ファイルを
    /// その橋渡しとして使う(<see cref="SceneTransitionButton"/> がガレージ側で出撃前に保存する)。
    /// </para>
    ///
    /// <para>
    /// <see cref="MechRuntime.Start"/> との実行順は保証しない。<see cref="MechRuntime.Apply"/> は
    /// 何度呼んでも安全なので、どちらが先に走っても最終的にはこのスクリプトが読み込んだ構成に
    /// 収束する(保存ファイルが無い/壊れているときは何もしないので、MechRuntime 自身の
    /// defaultPartIds によるフォールバックがそのまま残る)。
    /// </para>
    /// </summary>
    public sealed class TestFieldScreen : MonoBehaviour
    {
        [SerializeField] private MechRuntime mechRuntime;
        [SerializeField] private PartCatalog partCatalog;

        private void Start()
        {
            if (mechRuntime == null || partCatalog == null)
            {
                Debug.LogWarning("[TestFieldScreen] mechRuntime / partCatalog が未設定。保存構成の引き継ぎを行わない。", this);
                return;
            }

            LoadoutLoadResult result = LoadoutSaveFile.Load(partCatalog);

            // 警告を捨てない(D-17 の趣旨)。この画面にはステータスパネルが無いので、
            // 表示先はコンソールになるが、「保存したはずのパーツが消えている」ことに
            // 気づける経路は最低限残す ―― ガレージへ戻れば同じ内容が警告行にも出る。
            LogWarnings(result.Warnings);

            if (!result.Success || result.Loadouts == null || result.Loadouts.Count == 0)
            {
                // 保存が無い、または壊れている。MechRuntime.Start の defaultPartIds フォールバックに任せる。
                return;
            }

            int index = Mathf.Clamp(result.ActiveIndex, 0, result.Loadouts.Count - 1);
            mechRuntime.Apply(result.Loadouts[index], partCatalog);
        }

        private void LogWarnings(IReadOnlyList<string> warnings)
        {
            if (warnings == null)
            {
                return;
            }

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning($"[TestFieldScreen] {warnings[i]}", this);
            }
        }
    }
}
