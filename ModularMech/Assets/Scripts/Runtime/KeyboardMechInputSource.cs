using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 旧 Input Manager によるキーボード入力。
    /// v1 は「脚を変えると移動が変わる」ことの実証が目的なので、
    /// 入力周りは依存を最小にして Input System のセットアップ無しで動くようにしてある。
    ///
    /// MonoBehaviour ではないので、テストからは new して
    /// <see cref="MechLocomotionController.SetInputSource"/> に差し込める。
    /// </summary>
    public sealed class KeyboardMechInputSource : IMechInputSource
    {
        const string HorizontalAxis = "Horizontal";
        const string VerticalAxis = "Vertical";

        readonly KeyCode _sprintKey;
        readonly KeyCode _jumpKey;
        readonly KeyCode _crouchKey;
        readonly KeyCode _emoteKey;

        public KeyboardMechInputSource()
            : this(KeyCode.LeftShift, KeyCode.Space, KeyCode.LeftControl, KeyCode.G)
        {
        }

        public KeyboardMechInputSource(KeyCode sprintKey, KeyCode jumpKey, KeyCode crouchKey, KeyCode emoteKey)
        {
            _sprintKey = sprintKey;
            _jumpKey = jumpKey;
            _crouchKey = crouchKey;
            _emoteKey = emoteKey;
        }

        public MechInputState Read()
        {
            MechInputState state = default;

#if ENABLE_LEGACY_INPUT_MANAGER
            // GetAxisRaw なので加速の補間はここでは行わない。慣性は移動方式ごとの戦略が決める。
            float horizontal = Input.GetAxisRaw(HorizontalAxis);
            float vertical = Input.GetAxisRaw(VerticalAxis);

            var move = new Vector2(horizontal, vertical);
            float sqrMagnitude = move.sqrMagnitude;
            if (sqrMagnitude > 1f)
            {
                move /= Mathf.Sqrt(sqrMagnitude); // 斜め入力が速くならないよう丸める
            }

            state.move = move;
            state.sprint = Input.GetKey(_sprintKey);
            state.jump = Input.GetKeyDown(_jumpKey);   // 押しっぱなしで連続ジャンプしないよう Down のみ
            state.crouch = Input.GetKey(_crouchKey);
            state.emote = Input.GetKeyDown(_emoteKey);
#endif

            return state;
        }
    }
}
