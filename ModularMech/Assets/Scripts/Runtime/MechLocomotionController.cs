using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// 機体の移動(設計ドキュメント §5.2)。
    ///
    /// このクラスが見てよいのは <see cref="CapabilityFlags"/> と <see cref="ILocomotionProfileData"/> だけ。
    /// パーツ ID / パーツ種別で分岐しないので、パーツを追加してもここは変更不要になる。
    /// 移動方式ごとの差は <see cref="ILocomotionStrategy"/> 側にあり、
    /// このクラスは「能力によるロック」と「パーツ由来の数値の焼き込み」しか担当しない。
    ///
    /// Update 内では GetComponent / LINQ / 新規割り当てを行わない。
    /// 参照は Awake で、パーツ由来の数値は LoadoutApplied のたびに1度だけ更新する。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ModularMech/Mech Locomotion Controller")]
    public sealed class MechLocomotionController : MonoBehaviour
    {
        /// <summary>
        /// ペナルティ倍率の下限(CLAUDE.md D-15)。§3.3 の式そのものは変えず、消費側でだけ効かせる。
        ///
        /// <para>
        /// powerOutput = 0 かつ powerDraw &gt; 0 のとき PowerRatio は 0 になり、加速度も旋回速度も
        /// 0 倍になる。これを放置すると「入力しても永久に旋回できない」機体ができ、
        /// パーツ構成のせいなのか不具合なのかプレイヤーには切り分けられない。
        /// </para>
        /// <para>
        /// 速度・加速・旋回に<b>対称に</b>掛けること。片方にだけ下限を置くと
        /// 「這うように前進はするが決して向きが変わらない」という、より分かりにくい壊れ方をする。
        /// なお SpeedMultiplier は §3.3 の Lerp が 0.4 で下げ止まるためこの下限には触れないが、
        /// 対称性を崩さないために同じ形で書いておく。
        /// </para>
        /// </summary>
        const float MinPenaltyFactor = 0.1f;

        /// <summary>
        /// 加速度の絶対下限。ペナルティ由来の下限は <see cref="MinPenaltyFactor"/> が受け持つので、
        /// こちらは LocomotionProfile.acceleration が 0 のままのデータ不備に対する保険。
        /// 0 だと Vector3.MoveTowards が1フレームも進まず、入力しても永久に静止したままになる。
        /// </summary>
        const float MinAcceleration = 0.01f;

        /// <summary>
        /// 減速比の下限。0 以下だと一度動き出した機体が二度と止まらなくなるため、
        /// Inspector で 0 を入れられても最低限の制動は残す。
        /// </summary>
        const float MinDecelerationRatio = 0.1f;

        [Header("References")]
        [SerializeField] MechRuntime runtime;
        [SerializeField] CharacterController characterController;
        [SerializeField] MechAnimationDriver animationDriver;

        [Header("Tuning (機体共通。パーツ固有の値は LocomotionProfile 側)")]
        [SerializeField] float gravity = -20f;

        [Tooltip("Run 能力があるときのスプリント倍率。設計ドキュメント §5.2 の機体共通設定で、\n" +
                 "この値の出典はここ1箇所だけ(D-23)。移動戦略にコピーしないこと。")]
        [SerializeField] float runSpeedRatio = 1.6f;

        [Tooltip("接地維持のために毎フレーム与える下向き速度。0 だと接地判定が点滅する。")]
        [SerializeField] float groundedStickSpeed = -2f;

        [Tooltip("減速度 = 加速度 × この比率。1 より大きいと止まりやすい。")]
        [SerializeField] float decelerationRatio = 1.3f;

        [Tooltip("ホバーが高度を測るためのレイヤ。")]
        [SerializeField] LayerMask groundMask = ~0;

        [SerializeField] float groundProbeDistance = 20f;

        readonly MechLocomotionContext _context = new MechLocomotionContext();

        LocomotionStrategyRegistry _registry;
        ILocomotionStrategy _strategy;
        IMechInputSource _inputSource;
        bool _subscribed;

        /// <summary>現在の移動方式。脚が無ければ null。</summary>
        public ILocomotionStrategy CurrentStrategy => _strategy;

        public MechLocomotionContext Context => _context;

        public LocomotionStrategyRegistry Registry => _registry;

        /// <summary>
        /// スプリント倍率(設計ドキュメント §5.2)。ステータスパネルが副表示の「走行時 ×N」に
        /// 使う。表示と実挙動の出典を1つにするために公開している(D-23 / D-9)。
        /// </summary>
        public float RunSpeedRatio => runSpeedRatio;

        /// <summary>移動できる状態か。脚が無い / Walk も Hover も無い構成では false。</summary>
        public bool CanMove =>
            _strategy != null && _context.Profile != null &&
            (_context.Capabilities.Has(CapabilityFlags.Walk) || _context.Capabilities.Has(CapabilityFlags.Hover));

        void Awake()
        {
            if (runtime == null)
            {
                runtime = GetComponent<MechRuntime>();
            }

            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (animationDriver == null)
            {
                animationDriver = GetComponent<MechAnimationDriver>();
            }

            // MonoBehaviour で入力源を差し替えたい場合はここで拾う。
            // 無ければ旧 Input Manager のキーボードで動く。テストは SetInputSource で差し替える。
            _inputSource = GetComponent<IMechInputSource>();
            if (_inputSource == null)
            {
                _inputSource = new KeyboardMechInputSource();
            }

            _registry = LocomotionStrategyRegistry.CreateDefault();

            _context.Transform = transform;
            _context.Controller = characterController;
            _context.AnimationDriver = animationDriver;
            _context.Runtime = runtime;
        }

        void OnEnable()
        {
            Subscribe();
            RefreshFromRuntime();
        }

        void OnDisable()
        {
            Unsubscribe();

            if (_strategy != null)
            {
                _strategy.Exit(_context);
                _strategy = null;
            }
        }

        /// <summary>テストやリプレイから入力源を差し替える。</summary>
        public void SetInputSource(IMechInputSource source)
        {
            _inputSource = source ?? new KeyboardMechInputSource();
        }

        /// <summary>移動方式の実装を差し替える(モックや新方式の実験用)。</summary>
        public void RegisterStrategy(ILocomotionStrategy strategy)
        {
            if (_registry == null)
            {
                _registry = LocomotionStrategyRegistry.CreateDefault();
            }

            _registry.Register(strategy);
            RefreshFromRuntime();
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            MechInputState input = _inputSource.Read();

            if (_strategy == null || _context.Profile == null)
            {
                // 脚が無い / 移動方式が解決できない構成。例外にはせず、その場で落ちるだけにする。
                TickImmobile(deltaTime);
                return;
            }

            ApplyCapabilityLocks(ref input);

            // スプリント倍率の適用はここ1回だけ(CLAUDE.md D-23)。戦略に配ると、新しい移動方式の
            // 実装者が同じ乗算を書き忘れた瞬間に「その方式だけスプリントが効かない」という
            // 気づきにくい無反応(D-5 / D-18 が嫌ったもの)ができる。戦略には確定済みの速度を渡す。
            // sprint は ApplyCapabilityLocks が Run 能力で既に落としてあるので、ここでは能力を見ない。
            // 倍率は 1 未満に落とさない(Inspector に 1 未満を入れられても「走ると遅くなる」を作らない)。
            // StatPanelView も 1 以下の倍率では副表示を出さないので、表示と挙動の扱いが揃う。
            _context.EffectiveMoveSpeed = input.sprint
                ? _context.MaxMoveSpeed * Mathf.Max(1f, runSpeedRatio)
                : _context.MaxMoveSpeed;

            _context.JumpedThisFrame = false;
            _strategy.Tick(_context, in input, deltaTime);

            PushAnimation(input, deltaTime);
        }

        /// <summary>
        /// 能力によるロックを1箇所に集約する。
        /// 各戦略が能力を再判定すると、方式が増えるたびに同じ if が増えるため、必ずここで潰す。
        /// </summary>
        void ApplyCapabilityLocks(ref MechInputState input)
        {
            CapabilityFlags caps = _context.Capabilities;

            bool canTravel = caps.Has(CapabilityFlags.Walk) || caps.Has(CapabilityFlags.Hover);
            if (!canTravel)
            {
                input.move = Vector2.zero;
            }

            if (!caps.Has(CapabilityFlags.Run))
            {
                input.sprint = false;
            }

            if (!caps.Has(CapabilityFlags.Jump))
            {
                input.jump = false;
            }

            if (!caps.Has(CapabilityFlags.Crouch))
            {
                input.crouch = false;
            }

            if (!caps.Has(CapabilityFlags.Emote))
            {
                input.emote = false;
            }
        }

        /// <summary>脚が無いときの最低限の処理。重力だけ適用して、その場に居させる。</summary>
        void TickImmobile(float deltaTime)
        {
            _context.RefreshGrounded();
            _context.PlanarVelocity = Vector3.zero;
            _context.NormalizedSpeed = 0f;
            _context.TurnAmount = 0f;

            if (_context.IsGrounded && _context.VerticalVelocity < 0f)
            {
                _context.VerticalVelocity = groundedStickSpeed;
            }

            _context.VerticalVelocity += gravity * deltaTime;
            _context.ApplyMotion(new Vector3(0f, _context.VerticalVelocity * deltaTime, 0f));

            if (animationDriver != null)
            {
                animationDriver.SetLocomotion(0f, 0f, _context.IsGrounded, false, deltaTime);
            }
        }

        void PushAnimation(in MechInputState input, float deltaTime)
        {
            if (animationDriver == null)
            {
                return;
            }

            animationDriver.SetLocomotion(
                _context.NormalizedSpeed,
                _context.TurnAmount,
                _context.IsGrounded,
                _context.IsHovering,
                deltaTime);

            animationDriver.SetCrouch(input.crouch);

            if (_context.JumpedThisFrame)
            {
                animationDriver.TriggerJump();
            }

            if (input.emote)
            {
                animationDriver.TriggerEmote();
            }
        }

        void Subscribe()
        {
            if (_subscribed || runtime == null)
            {
                return;
            }

            runtime.LoadoutApplied += RefreshFromRuntime;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || runtime == null)
            {
                return;
            }

            runtime.LoadoutApplied -= RefreshFromRuntime;
            _subscribed = false;
        }

        /// <summary>
        /// パーツ由来の数値をコンテキストへ焼き込む。Loadout 適用時にだけ走る。
        /// Update から MechRuntime を辿らないのは、毎フレームのプロパティ追跡を避けるため。
        /// </summary>
        public void RefreshFromRuntime()
        {
            if (_registry == null)
            {
                _registry = LocomotionStrategyRegistry.CreateDefault();
            }

            ILocomotionProfileData profile = runtime != null ? runtime.Locomotion : null;
            StatBlock stats = runtime != null ? runtime.CurrentStats : StatBlock.Empty;

            _context.Profile = profile;
            _context.Capabilities = runtime != null ? runtime.Capabilities : CapabilityFlags.None;
            _context.Gravity = gravity;
            _context.GroundedStickSpeed = groundedStickSpeed;
            _context.GroundMask = groundMask;
            _context.GroundProbeDistance = groundProbeDistance;

            if (profile == null)
            {
                // 脚が無い構成。移動不能として成立させる(例外は投げない)。
                SwitchStrategy(null);
                _context.MaxMoveSpeed = 0f;
                _context.EffectiveMoveSpeed = 0f;
                _context.TurnSpeed = 0f;
                _context.JumpPower = 0f;
                _context.Acceleration = 0f;
                _context.Deceleration = 0f;
                _context.GroundOffset = 0f;
                _context.PlanarVelocity = Vector3.zero;
                return;
            }

            // ペナルティ倍率は §3.3 の値をそのまま使い、消費側でだけ下限を掛ける(D-15)。
            // 速度・加速・旋回に同じ下限を対称に適用する。
            float speedFactor = Mathf.Max(MinPenaltyFactor, stats.SpeedMultiplier);
            float turnFactor = Mathf.Max(MinPenaltyFactor, stats.TurnSpeedMultiplier);
            float accelerationFactor = Mathf.Max(MinPenaltyFactor, stats.AccelerationMultiplier);

            // 加算補正(パーツ)→ 乗算ペナルティ(過積載/パワー不足)の順。§3.3 の適用順を崩さない。
            // ここで焼き込んだ MaxMoveSpeed は StatPanelView が主表示する実効速度と同じ値になる。
            // 移動戦略はこの値に倍率を掛けてはならない(D-14)。
            _context.MaxMoveSpeed = Mathf.Max(0f, (profile.BaseMoveSpeed + stats.Raw.moveSpeedMod) * speedFactor);

            // Update が走る前に戦略が Enter で参照しても 0 にならないよう、非スプリント時の値で初期化する。
            // 毎フレームの確定値は Update が入力を読んでから入れ直す(D-23)。
            _context.EffectiveMoveSpeed = _context.MaxMoveSpeed;
            _context.TurnSpeed = Mathf.Max(0f, (profile.BaseTurnSpeed + stats.Raw.turnSpeedMod) * turnFactor);
            _context.JumpPower = Mathf.Max(0f, profile.BaseJumpPower + stats.Raw.jumpPowerMod);
            _context.GroundOffset = profile.GroundOffset;

            // パワー不足は加速度に効く(§3.3)。
            // PartStats.stability はここでは読まない(v1 の予約フィールド。CLAUDE.md D-4)。
            float acceleration = Mathf.Max(MinAcceleration, profile.Acceleration * accelerationFactor);
            _context.Acceleration = acceleration;
            _context.Deceleration = acceleration * Mathf.Max(MinDecelerationRatio, decelerationRatio);

            SwitchStrategy(_registry.Resolve(profile.Type));
        }

        void SwitchStrategy(ILocomotionStrategy next)
        {
            if (ReferenceEquals(_strategy, next))
            {
                return;
            }

            if (_strategy != null)
            {
                _strategy.Exit(_context);
            }

            _strategy = next;

            if (_strategy != null)
            {
                _strategy.Enter(_context);
            }
        }
    }
}
