using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 1フレーム分の操作入力。入力デバイスと移動ロジックの間の唯一の受け渡し面。
    /// struct なのは、毎フレーム読み出しても GC を発生させないため。
    /// </summary>
    public struct MechInputState
    {
        /// <summary>x = 旋回(-1 左 / +1 右)、y = 前後(+1 前進)。長さは 1 以内に丸めて渡す。</summary>
        public Vector2 move;

        public bool sprint;
        public bool jump;
        public bool crouch;
        public bool emote;

        /// <summary>入力なしの状態。能力不足でロックするときに使う。</summary>
        public static MechInputState Idle => default;

        public override string ToString()
        {
            return $"move=({move.x:0.##},{move.y:0.##}) sprint={sprint} jump={jump} crouch={crouch} emote={emote}";
        }
    }
}
