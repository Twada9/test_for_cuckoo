using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// アニメーションの差し替えとパラメータ更新(設計ドキュメント §5.3)。
    ///
    /// ベースの <see cref="RuntimeAnimatorController"/> は1つだけ作り、
    /// 脚パーツの <c>LocomotionProfile.animatorSet</c>(AnimatorOverrideController)でクリップだけ入れ替える。
    /// ステートマシンの保守対象が1つで済むのが狙い。
    ///
    /// 適用する AOC は **脚由来の1枚だけ**(CLAUDE.md D-3)。
    /// AOC はベースコントローラのクリップを鍵にした差し替え表なので、腕の AOC を後から重ねても
    /// 鍵が一致せず黙って無効になる。腕パーツの見た目差はプレハブ形状で表現する。
    ///
    /// パラメータ名は <see cref="Parameters"/> に一元管理し、ハッシュは静的にキャッシュする。
    /// 存在しないパラメータへの書き込みは毎フレーム警告を吐くため、
    /// コントローラ差し替えのたびに存在確認を1度だけ行う。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ModularMech/Mech Animation Driver")]
    public sealed class MechAnimationDriver : MonoBehaviour
    {
        /// <summary>ベース AnimatorController が持つパラメータ名。ここ以外に文字列を書かない。</summary>
        public static class Parameters
        {
            public const string Speed = "Speed";
            public const string Turn = "Turn";
            public const string Grounded = "Grounded";
            public const string Hovering = "Hovering";
            public const string Crouch = "Crouch";
            public const string Jump = "Jump";
            public const string Emote = "Emote";
            public const string LocomotionType = "LocomotionType";
        }

        static readonly int SpeedHash = Animator.StringToHash(Parameters.Speed);
        static readonly int TurnHash = Animator.StringToHash(Parameters.Turn);
        static readonly int GroundedHash = Animator.StringToHash(Parameters.Grounded);
        static readonly int HoveringHash = Animator.StringToHash(Parameters.Hovering);
        static readonly int CrouchHash = Animator.StringToHash(Parameters.Crouch);
        static readonly int JumpHash = Animator.StringToHash(Parameters.Jump);
        static readonly int EmoteHash = Animator.StringToHash(Parameters.Emote);
        static readonly int LocomotionTypeHash = Animator.StringToHash(Parameters.LocomotionType);

        [Header("References")]
        [SerializeField] Animator animator;

        [Tooltip("すべての機体で共有するベース AnimatorController。脚の animatorSet はこれの上に被せる。")]
        [SerializeField] RuntimeAnimatorController baseController;

        [Header("Smoothing")]
        [Tooltip("Speed パラメータのダンプ時間。0 にすると歩行ブレンドがガタつく。")]
        [SerializeField] float speedDampTime = 0.12f;

        [SerializeField] float turnDampTime = 0.08f;

        bool _hasSpeed;
        bool _hasTurn;
        bool _hasGrounded;
        bool _hasHovering;
        bool _hasCrouch;
        bool _hasJump;
        bool _hasEmote;
        bool _hasLocomotionType;

        RuntimeAnimatorController _appliedController;

        /// <summary>
        /// 「適用できる AnimatorController が無い」警告を出したか。
        /// ベース AnimatorController が未作成の間、Loadout 適用のたびに同じ警告が積まれ、
        /// D-2 が可視化したいボーン不一致の警告(1回きり)をコンソールから押し流してしまうため、
        /// この警告は1回だけにする。コントローラが割り当てられたら畳み直す。
        /// </summary>
        bool _warnedMissingController;

        public Animator Animator => animator;

        /// <summary>現在適用されているコントローラ。テストから差し替え結果を確認するために公開する。</summary>
        public RuntimeAnimatorController AppliedController => _appliedController;

        void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (baseController == null && animator != null)
            {
                // Inspector 未設定なら、シーンに置かれている時点のコントローラをベースとみなす。
                baseController = animator.runtimeAnimatorController;
            }

            CacheParameters();
        }

        /// <summary>
        /// Loadout 適用時に呼ばれる。脚の <c>animatorSet</c> があればそれを、無ければベースを適用する。
        /// 同じコントローラなら再代入しない(再代入はステートマシンのリセット = 一瞬の T ポーズ)。
        /// </summary>
        public void ApplyLoadout(ILocomotionProfileData profile)
        {
            if (animator == null)
            {
                return;
            }

            // 移動は完全にスクリプト駆動。アニメーションに位置を動かさせない(CLAUDE.md D-7)。
            animator.applyRootMotion = false;

            RuntimeAnimatorController next = baseController;

            var locomotionProfile = profile as LocomotionProfile;
            if (locomotionProfile != null && locomotionProfile.animatorSet != null)
            {
                next = locomotionProfile.animatorSet;
            }

            if (next == null)
            {
                // アニメータ未設定でも移動そのものは成立させる。警告だけ出して黙って進む。
                if (!_warnedMissingController)
                {
                    _warnedMissingController = true;
                    Debug.LogWarning(
                        "[MechAnimationDriver] 適用できる AnimatorController が無い。アニメーションは更新されない。" +
                        "(この警告は最初の1回だけ出す)", this);
                }
                return;
            }

            // 一度でも適用できたら、次に無くなったときは改めて知らせる。
            _warnedMissingController = false;

            bool controllerChanged =
                !ReferenceEquals(_appliedController, next) || !ReferenceEquals(animator.runtimeAnimatorController, next);

            if (controllerChanged)
            {
                animator.runtimeAnimatorController = next;
                _appliedController = next;

                // クリップ表が変わるとパラメータ構成も変わり得るので、存在確認をやり直す。
                CacheParameters();
            }

            // コントローラが同じ(= 例えば二脚どうしの脚換装で animatorSet がどちらも未設定)場合でも、
            // LocomotionType パラメータは毎回更新する。ここを早期 return の内側に置くと、
            // 「脚を替えたのにステートマシン側の分岐が前の値のまま固まる」という実行時の
            // 黙った失敗になる(T ポーズ回避のためのコントローラ再代入スキップとは独立の話)。
            SetLocomotionType(profile != null ? profile.Type : ModularMech.Data.LocomotionType.Biped);
        }

        /// <summary>毎フレーム呼ばれる。ここでの割り当てを避けるため、値渡しの引数のみで受ける。</summary>
        public void SetLocomotion(float normalizedSpeed, float turnAmount, bool grounded, bool hovering, float deltaTime)
        {
            if (animator == null || _appliedController == null)
            {
                return;
            }

            if (_hasSpeed)
            {
                animator.SetFloat(SpeedHash, normalizedSpeed, speedDampTime, deltaTime);
            }

            if (_hasTurn)
            {
                animator.SetFloat(TurnHash, turnAmount, turnDampTime, deltaTime);
            }

            if (_hasGrounded)
            {
                animator.SetBool(GroundedHash, grounded);
            }

            if (_hasHovering)
            {
                animator.SetBool(HoveringHash, hovering);
            }
        }

        public void SetCrouch(bool crouching)
        {
            if (animator == null || !_hasCrouch)
            {
                return;
            }

            animator.SetBool(CrouchHash, crouching);
        }

        public void TriggerJump()
        {
            if (animator == null || !_hasJump)
            {
                return;
            }

            animator.SetTrigger(JumpHash);
        }

        public void TriggerEmote()
        {
            if (animator == null || !_hasEmote)
            {
                return;
            }

            animator.SetTrigger(EmoteHash);
        }

        /// <summary>移動方式を int パラメータとして渡す。ステートマシン側で分岐したい場合に使う。</summary>
        public void SetLocomotionType(LocomotionType type)
        {
            if (animator == null || !_hasLocomotionType)
            {
                return;
            }

            animator.SetInteger(LocomotionTypeHash, (int)type);
        }

        /// <summary>
        /// パラメータの存在を1度だけ調べる。
        /// 存在しないパラメータへの Set は毎フレーム警告を出し、コンソールが埋まって
        /// 本当の警告(ボーン不一致など)が見えなくなるため。
        /// </summary>
        void CacheParameters()
        {
            _hasSpeed = false;
            _hasTurn = false;
            _hasGrounded = false;
            _hasHovering = false;
            _hasCrouch = false;
            _hasJump = false;
            _hasEmote = false;
            _hasLocomotionType = false;

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            _appliedController = animator.runtimeAnimatorController;

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                int hash = parameters[i].nameHash;

                if (hash == SpeedHash) _hasSpeed = true;
                else if (hash == TurnHash) _hasTurn = true;
                else if (hash == GroundedHash) _hasGrounded = true;
                else if (hash == HoveringHash) _hasHovering = true;
                else if (hash == CrouchHash) _hasCrouch = true;
                else if (hash == JumpHash) _hasJump = true;
                else if (hash == EmoteHash) _hasEmote = true;
                else if (hash == LocomotionTypeHash) _hasLocomotionType = true;
            }
        }
    }
}
