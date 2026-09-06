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
    ///
    /// <para>
    /// 旧 Input Manager が無効(<c>ENABLE_LEGACY_INPUT_MANAGER</c> 未定義)のときは、
    /// 黙ってゼロを返さずに1回だけエラーを出す(CLAUDE.md D-18)。
    /// Input System 導入時に「新しい入力バックエンドを有効にする」を選ぶと、
    /// コンパイルは通ったまま機体が一切動かなくなり、ログも一行も出ないため原因究明が極めて難しい。
    /// </para>
    /// </summary>
    public sealed class KeyboardMechInputSource : IMechInputSource
    {
        const string HorizontalAxis = "Horizontal";
        const string VerticalAxis = "Vertical";

#if !ENABLE_LEGACY_INPUT_MANAGER
        /// <summary>
        /// バックエンド不在の通知は1回だけにする。毎フレーム出すとコンソールが埋まって
        /// 他のエラーが読めなくなる。static なのは、入力源を作り直しても再通知しないため
        /// (ドメインリロードで戻るので、設定を直したかどうかの確認は再生し直せばよい)。
        /// </summary>
        static bool _backendErrorReported;
#endif

        // 旧 Input Manager が無効なビルドではこれらを読む箇所が消えるため CS0414 が出る。
        // コンストラクタの引数は公開契約なので削らず、その構成でだけ警告を黙らせる。
#if !ENABLE_LEGACY_INPUT_MANAGER
#pragma warning disable 0414
#endif
        readonly KeyCode _sprintKey;
        readonly KeyCode _jumpKey;
        readonly KeyCode _crouchKey;
        readonly KeyCode _emoteKey;
#if !ENABLE_LEGACY_INPUT_MANAGER
#pragma warning restore 0414
#endif

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
#else
            // D-5(移動方式が解決できないときは警告を出してフォールバックする)の入力版。
            // 無反応を作らない: 動かない原因がここにあることを、最初の1回で必ず知らせる。
            if (!_backendErrorReported)
            {
                _backendErrorReported = true;
                Debug.LogError(
                    "[KeyboardMechInputSource] 旧 Input Manager が無効(ENABLE_LEGACY_INPUT_MANAGER が未定義)のため、" +
                    "キーボード入力を一切読み取れません。機体はキーを押しても動きません。\n" +
                    "対処: Project Settings > Player > Active Input Handling を 'Both'(または 'Input Manager (Old)')にするか、" +
                    "Input System 用の IMechInputSource を実装して " +
                    "MechLocomotionController.SetInputSource() で差し込んでください。");
            }
#endif

            return state;
        }
    }
}
