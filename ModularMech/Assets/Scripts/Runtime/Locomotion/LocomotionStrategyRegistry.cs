using System.Collections.Generic;
using ModularMech.Data;
using UnityEngine;

namespace ModularMech.Mechs
{
    /// <summary>
    /// <see cref="LocomotionType"/> から <see cref="ILocomotionStrategy"/> を引く表。
    ///
    /// **新しい移動方式の追加は「クラス1つ + <see cref="CreateDefault"/> への登録1行」で済むこと。**
    /// コントローラ側に switch を書かないための唯一の窓口であり、
    /// ここ以外に LocomotionType の分岐を作らない。
    /// </summary>
    public sealed class LocomotionStrategyRegistry
    {
        readonly Dictionary<LocomotionType, ILocomotionStrategy> _strategies =
            new Dictionary<LocomotionType, ILocomotionStrategy>(4);

        /// <summary>解決できなかったときに使う方式。未知の LocomotionType でも動きは止めない。</summary>
        public ILocomotionStrategy Fallback { get; set; }

        /// <summary>v1 の標準構成。移動方式を足すときはここに1行足す。</summary>
        public static LocomotionStrategyRegistry CreateDefault()
        {
            var registry = new LocomotionStrategyRegistry();
            registry.Register(new BipedLocomotionStrategy());
            registry.Register(new QuadrupedLocomotionStrategy());
            registry.Register(new HoverLocomotionStrategy());
            registry.Register(new TrackedLocomotionStrategy());
            registry.Fallback = registry.Resolve(LocomotionType.Biped);
            return registry;
        }

        /// <summary>同じ <see cref="ILocomotionStrategy.Type"/> が既にあれば上書きする(差し替え用)。</summary>
        public void Register(ILocomotionStrategy strategy)
        {
            if (strategy == null)
            {
                Debug.LogWarning("[LocomotionStrategyRegistry] null の戦略は登録できない。");
                return;
            }

            _strategies[strategy.Type] = strategy;
        }

        public bool TryGet(LocomotionType type, out ILocomotionStrategy strategy)
        {
            return _strategies.TryGetValue(type, out strategy);
        }

        /// <summary>
        /// 見つからなければ <see cref="Fallback"/> を返す(それも無ければ null)。
        /// 未登録の方式で例外を投げると、脚を1つ足しただけで機体が動かなくなるため。
        /// </summary>
        public ILocomotionStrategy Resolve(LocomotionType type)
        {
            if (_strategies.TryGetValue(type, out ILocomotionStrategy strategy))
            {
                return strategy;
            }

            if (Fallback != null)
            {
                Debug.LogWarning($"[LocomotionStrategyRegistry] {type} の戦略が未登録。{Fallback.Type} で代用する。");
            }

            return Fallback;
        }
    }
}
