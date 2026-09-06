using UnityEngine;
using UnityEngine.UI;

namespace ModularMech.UI
{
    /// <summary>
    /// ステータスパネルの能力アイコン1枠。「付与されている」と「付与されたがペナルティで
    /// 剥奪された」を見た目で区別するためだけの小さなビュー(CLAUDE.md D-9)。
    /// 「そもそも付与されていない」能力はこのビュー自体が表示されない(一覧から除外される)。
    ///
    /// <para>
    /// アイコン画像と併せて <see cref="CapabilityIconSet.Entry.DisplayName"/> を必ず出す。
    /// v1 のプレースホルダは <c>CapabilityIconSet</c> のアイコンが全件未設定(null)で、
    /// 絵だけに頼ると最大9個の同じ白い四角が並び、<b>どの能力が剥奪されたのか判別できない</b>
    /// ―― §11-3「能力が表示される」が実質満たせなくなるため。
    /// </para>
    /// </summary>
    public sealed class CapabilityIconView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;

        [Tooltip("能力の表示名(CapabilityIconSet.displayName)。アイコン未設定でも識別できるようにする。")]
        [SerializeField] private Text labelText;

        [Tooltip("剥奪時に出す取り消し線などのオーバーレイ。任意で、無ければ色の変化だけで表現する。")]
        [SerializeField] private GameObject strippedOverlay;

        [SerializeField] private Color activeColor = Color.white;
        [SerializeField] private Color strippedColor = new Color(1f, 1f, 1f, 0.35f);

        /// <param name="icon">表示するアイコン。v1 のプレースホルダでは null になり得る。</param>
        /// <param name="displayName">能力の表示名。アイコンが無くても何の能力か分かるようにする。</param>
        /// <param name="active">
        /// true: 現在も有効な能力。false: 付与はされているが過積載/パワー不足のペナルティで
        /// 剥奪されている(無表示にすると「脚が壊れて見える」ため、暗い表示で明示する)。
        /// </param>
        public void SetState(Sprite icon, string displayName, bool active)
        {
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.color = active ? activeColor : strippedColor;
            }

            if (labelText != null)
            {
                labelText.text = displayName;
                labelText.color = active ? activeColor : strippedColor;
            }

            if (strippedOverlay != null)
            {
                strippedOverlay.SetActive(!active);
            }
        }
    }
}
