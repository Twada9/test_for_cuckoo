using System;
using ModularMech.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ModularMech.UI
{
    /// <summary>
    /// 装備可能パーツ一覧の1件。<see cref="PartListView"/> がプールして使い回す
    /// (スロット切り替えのたびに Destroy/Instantiate しない)。
    /// 装備不可の理由がある場合はボタンを非活性にし、理由テキストを表示する。
    /// <see cref="SetContent"/> に part=null を渡すと「装備しない」選択肢として表示する
    /// (<see cref="PartListView"/> がスロットの先頭に必ず1件差し込む)。
    /// </summary>
    public sealed class PartEntryView : MonoBehaviour
    {
        private const string EmptyOptionLabel = "(装備しない)";

        [SerializeField] private Image iconImage;
        [SerializeField] private Text nameText;
        [SerializeField] private Text reasonText;
        [SerializeField] private GameObject equippedHighlight;
        [SerializeField] private Button button;

        private PartDefinition _part;
        private Action<PartDefinition> _onClicked;

        public void Initialize(Action<PartDefinition> onClicked)
        {
            _onClicked = onClicked;
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }
        }

        public void SetActiveEntry(bool active)
        {
            gameObject.SetActive(active);
        }

        /// <summary>
        /// 表示内容を更新する。
        /// </summary>
        /// <param name="part">表示するパーツ定義。</param>
        /// <param name="equippable">装備可能かどうか。false ならボタンを非活性にする。</param>
        /// <param name="reasonIfBlocked">装備不可の理由(日本語)。equippable が true の場合は無視される。</param>
        /// <param name="isEquipped">現在このスロットに装備中かどうか。</param>
        public void SetContent(PartDefinition part, bool equippable, string reasonIfBlocked, bool isEquipped)
        {
            _part = part;

            if (nameText != null)
            {
                nameText.text = part != null ? part.displayName : EmptyOptionLabel;
            }

            if (iconImage != null)
            {
                Sprite icon = part != null ? part.icon : null;
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            if (button != null)
            {
                button.interactable = equippable;
            }

            if (reasonText != null)
            {
                bool showReason = !equippable && !string.IsNullOrEmpty(reasonIfBlocked);
                reasonText.gameObject.SetActive(showReason);
                reasonText.text = showReason ? reasonIfBlocked : string.Empty;
            }

            if (equippedHighlight != null)
            {
                equippedHighlight.SetActive(isEquipped);
            }
        }

        private void HandleClick()
        {
            // _part が null の場合もある(先頭の「装備しない」エントリ)。それも有効なクリックとして
            // そのまま通知する — PartListView/GarageScreen 側が null=unequip として解釈する。
            // 非活性(equippable=false)なボタンは Unity 側がそもそもクリックを発生させない。
            _onClicked?.Invoke(_part);
        }
    }
}
