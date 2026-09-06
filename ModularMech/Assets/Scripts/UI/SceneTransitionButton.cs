using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModularMech.UI
{
    /// <summary>
    /// ガレージ ⇔ テストフィールドの導線用の最小限のシーン遷移ボタン(設計ドキュメント §6.2)。
    ///
    /// v1 にはシーン遷移専用のマネージャが無い。<c>SceneManager.LoadScene</c> は static メソッドなので
    /// uGUI の Button.onClick(永続リスナー)には直接登録できず(登録には対象となる
    /// UnityEngine.Object のインスタンスが要る)、この薄いラッパーで仲介する。
    ///
    /// GarageSceneBuilder / TestFieldSceneBuilder が Editor スクリプトから生成・結線する前提の
    /// 補助コンポーネント。1ファイル1公開型の規約に合わせ、シーン遷移専用に責務を絞ってある。
    /// </summary>
    public sealed class SceneTransitionButton : MonoBehaviour
    {
        [Tooltip("読み込み先シーン名。File > Build Settings に追加されている必要がある。")]
        [SerializeField] private string sceneName;

        [Tooltip("設定した場合、GarageScreen.CanDeploy が true のときだけ遷移する(出撃ボタン用)。")]
        [SerializeField] private GarageScreen requireDeployableFrom;

        [Tooltip("設定した場合、遷移前に GarageScreen.SaveToDisk() を呼ぶ。" +
                 "テストフィールド側の TestFieldScreen が同じ保存ファイルを読み込むための橋渡し。")]
        [SerializeField] private GarageScreen saveBeforeLoad;

        /// <summary>Button.onClick から呼ぶ。</summary>
        public void LoadScene()
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("[SceneTransitionButton] sceneName が未設定。", this);
                return;
            }

            if (requireDeployableFrom != null && !requireDeployableFrom.CanDeploy)
            {
                Debug.LogWarning(
                    "[SceneTransitionButton] 現在の構成では出撃できない" +
                    "(必須スロット未装備などのエラーが残っている)。遷移しない。", this);
                return;
            }

            if (saveBeforeLoad != null)
            {
                saveBeforeLoad.SaveToDisk();
            }

            SceneManager.LoadScene(sceneName);
        }
    }
}
