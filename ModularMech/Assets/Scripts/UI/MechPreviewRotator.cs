using UnityEngine;
using UnityEngine.EventSystems;

namespace ModularMech.UI
{
    /// <summary>
    /// 3Dプレビューのドラッグ回転。ポインタの水平ドラッグ量だけを見て、指定した Transform を
    /// 鉛直軸まわりに回す。上下方向のチルトは設計ドキュメント §6.1 のスコープ外(「回転可能」のみ要求)。
    ///
    /// uGUI の EventSystem を経由するドラッグハンドラなので、プレビュー領域には Raycast Target が
    /// 有効な Image (または透明な受け皿 Image) を重ねてこのコンポーネントを付ける前提。
    /// </summary>
    public sealed class MechPreviewRotator : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("回転させる対象。機体の見た目全体を束ねるピボットを指す。")]
        [SerializeField] private Transform target;

        [SerializeField] private float degreesPerPixel = 0.3f;
        [SerializeField] private bool invertDirection;

        private bool _dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = target != null;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || target == null)
            {
                return;
            }

            float sign = invertDirection ? 1f : -1f;
            float degrees = eventData.delta.x * degreesPerPixel * sign;
            target.Rotate(Vector3.up, degrees, Space.World);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
        }
    }
}
