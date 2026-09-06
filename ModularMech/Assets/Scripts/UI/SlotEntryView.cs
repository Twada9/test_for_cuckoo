using System;
using ModularMech.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ModularMech.UI
{
    /// <summary>
    /// スロット一覧の1行。装備中パーツ名・アイコン・選択状態を表示する。
    /// <see cref="SlotListView"/> がスロットの数(8個固定)だけ起動時に生成して使い回すため、
    /// 装備変更のたびに Destroy/Instantiate はしない。
    /// </summary>
    public sealed class SlotEntryView : MonoBehaviour
    {
        private const string EmptyLabel = "(未装備)";

        [SerializeField] private Text slotNameText;
        [SerializeField] private Text partNameText;
        [SerializeField] private Image iconImage;
        [SerializeField] private Sprite emptyIconSprite;
        [SerializeField] private GameObject selectedHighlight;
        [SerializeField] private Button button;

        private PartSlot _slot;
        private Action<PartSlot> _onClicked;

        /// <summary>スロット一覧生成時に一度だけ呼ぶ。以後は <see cref="SetContent"/> / <see cref="SetSelected"/> だけを使う。</summary>
        public void Initialize(PartSlot slot, Action<PartSlot> onClicked)
        {
            _slot = slot;
            _onClicked = onClicked;

            if (slotNameText != null)
            {
                slotNameText.text = DisplayName(slot);
            }

            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }

            SetContent(null);
            SetSelected(false);
        }

        /// <summary>
        /// 装備中パーツの表示を更新する。<paramref name="part"/> が null なら未装備表示にする。
        /// IPartData 自体は表示名/アイコンを持たない(ゲームロジック用の読み取り面のため)ので、
        /// 実体である PartDefinition へキャストできた場合だけ名前とアイコンを補う。
        /// </summary>
        public void SetContent(IPartData part)
        {
            string label = EmptyLabel;
            Sprite icon = emptyIconSprite;

            if (part != null)
            {
                label = part.PartId;
                if (part is PartDefinition definition)
                {
                    if (!string.IsNullOrEmpty(definition.displayName))
                    {
                        label = definition.displayName;
                    }
                    if (definition.icon != null)
                    {
                        icon = definition.icon;
                    }
                }
            }

            if (partNameText != null)
            {
                partNameText.text = label;
            }

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }
        }

        public void SetSelected(bool selected)
        {
            if (selectedHighlight != null)
            {
                selectedHighlight.SetActive(selected);
            }
        }

        private void HandleClick()
        {
            _onClicked?.Invoke(_slot);
        }

        private static string DisplayName(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Head: return "頭部";
                case PartSlot.Torso: return "胴体";
                case PartSlot.ArmLeft: return "左腕";
                case PartSlot.ArmRight: return "右腕";
                case PartSlot.Legs: return "脚部";
                case PartSlot.Backpack: return "バックパック";
                case PartSlot.HandLeft: return "左手持ち";
                case PartSlot.HandRight: return "右手持ち";
                default: return slot.ToString();
            }
        }
    }
}
