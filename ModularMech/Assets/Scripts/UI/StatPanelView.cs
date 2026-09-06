using System;
using System.Collections.Generic;
using ModularMech.Data;
using ModularMech.Loadouts;
using ModularMech.Mechs;
using UnityEngine;
using UnityEngine.UI;

namespace ModularMech.UI
{
    /// <summary>
    /// 総重量/重量上限、消費電力/出力、速度、能力アイコンを表示する。
    /// MechRuntime.LoadoutApplied を受けて <see cref="Refresh"/> が呼ばれたときだけ更新し、
    /// 毎フレームは何もしない(CLAUDE.md D-10: Validate/集約結果はキャッシュを読むだけ)。
    ///
    /// 表示規約(CLAUDE.md D-9。逸脱禁止):
    /// <list type="bullet">
    /// <item>速度: ペナルティ適用後の実効値を主表示、基礎値を副表示(減っているときだけ)。</item>
    /// <item>重量: 総重量/上限。WeightCapacity&lt;=0(脚未装備等)は「—」であって「0%」ではない。</item>
    /// <item>電力: 出力/消費の順。比率(PowerRatio) 1.0 未満で色を変える。</item>
    /// <item>能力アイコン: 「そもそも付与されていない」(一覧に出ない)と
    /// 「付与されたがペナルティで剥奪された」(暗く表示)を区別する。</item>
    /// <item>警告行: ValidationIssue.Message を1件1行、Severity で色分け。</item>
    /// </list>
    /// </summary>
    public sealed class StatPanelView : MonoBehaviour
    {
        [Header("重量")]
        [SerializeField] private Text weightText;
        [SerializeField] private Image weightBarFill;

        [Tooltip("任意。100%を超えた分だけを重ねて示すオーバーフロー用バー。無ければ使わない。")]
        [SerializeField] private Image weightOverflowBarFill;

        [Header("電力")]
        [SerializeField] private Text powerText;
        [SerializeField] private Image powerBarFill;

        [Header("速度")]
        [SerializeField] private Text speedText; // 主表示(実効値)

        [Tooltip("副表示(基礎値)。ペナルティが無いときは空にして隠す。任意。")]
        [SerializeField] private Text speedBaseText;

        [Header("色")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warningColor = new Color(1f, 0.65f, 0f);
        [SerializeField] private Color errorColor = Color.red;

        [Header("能力アイコン")]
        [SerializeField] private CapabilityIconSet capabilityIconSet;
        [SerializeField] private Transform capabilityIconContainer;
        [SerializeField] private CapabilityIconView capabilityIconPrefab;

        [Header("警告/エラー一覧(LoadoutValidation.Issues)")]
        [SerializeField] private Transform issueListContainer;
        [SerializeField] private Text issueTextPrefab;

        private readonly List<CapabilityIconView> _capabilityIconPool = new List<CapabilityIconView>();
        private readonly List<Text> _issueTextPool = new List<Text>();

        /// <summary>
        /// MechRuntime.LoadoutApplied のハンドラから呼ぶ想定。
        /// </summary>
        /// <param name="rawCapabilities">
        /// ペナルティ適用前の(本来付与されている)能力の合計。StatBlock.Capabilities はペナルティ
        /// 適用後の値しか持たないため、「剥奪された」表示のために呼び出し側(GarageScreen)で
        /// 別途集約して渡してもらう。
        /// </param>
        public void Refresh(StatBlock stats, LoadoutValidation validation, ILocomotionProfileData locomotion, CapabilityFlags rawCapabilities)
        {
            RefreshWeight(stats);
            RefreshPower(stats);
            RefreshSpeed(stats, locomotion);
            RefreshCapabilities(rawCapabilities, stats.Capabilities);
            RefreshIssues(validation);
        }

        private void RefreshWeight(StatBlock stats)
        {
            bool hasCapacity = stats.WeightCapacity > 0f;
            float ratio = hasCapacity ? stats.TotalWeight / stats.WeightCapacity : 0f;

            if (weightText != null)
            {
                // WeightCapacity<=0 (脚未装備等)は上限が定まらないので「—」。0%に見せると
                // 「上限無制限」と誤読されるため、数値の0とは明確に書き分ける(D-9)。
                weightText.text = hasCapacity
                    ? $"重量 {stats.TotalWeight:0.0} / {stats.WeightCapacity:0.0}"
                    : $"重量 {stats.TotalWeight:0.0} / —";
                weightText.color = stats.IsOverweight ? errorColor : normalColor;
            }

            if (weightBarFill != null)
            {
                weightBarFill.gameObject.SetActive(hasCapacity);
                if (hasCapacity)
                {
                    weightBarFill.fillAmount = Mathf.Clamp01(ratio);
                    weightBarFill.color = stats.IsOverweight ? errorColor : normalColor;
                }
            }

            if (weightOverflowBarFill != null)
            {
                bool overflow = hasCapacity && ratio > 1f;
                weightOverflowBarFill.gameObject.SetActive(overflow);
                if (overflow)
                {
                    // 100%を超えた分だけを別バーで示す。100%を超えて伸びるバー表現の代替(D-9)。
                    weightOverflowBarFill.fillAmount = Mathf.Clamp01(ratio - 1f);
                    weightOverflowBarFill.color = errorColor;
                }
            }
        }

        private void RefreshPower(StatBlock stats)
        {
            if (powerText != null)
            {
                // D-9: 表示順は「出力 / 消費」(TotalPowerOutput / TotalPowerDraw)。
                powerText.text = $"電力 {stats.TotalPowerOutput:0.0} / {stats.TotalPowerDraw:0.0}";
                powerText.color = stats.IsUnderpowered ? warningColor : normalColor;
            }

            if (powerBarFill != null)
            {
                float ratio = stats.TotalPowerOutput > 0f ? stats.TotalPowerDraw / stats.TotalPowerOutput : 0f;
                powerBarFill.fillAmount = Mathf.Clamp01(ratio);
                powerBarFill.color = stats.IsUnderpowered ? warningColor : normalColor;
            }
        }

        private void RefreshSpeed(StatBlock stats, ILocomotionProfileData locomotion)
        {
            if (speedText == null && speedBaseText == null)
            {
                return;
            }

            if (locomotion == null)
            {
                if (speedText != null)
                {
                    speedText.text = "速度 —";
                    speedText.color = normalColor;
                }
                if (speedBaseText != null)
                {
                    speedBaseText.gameObject.SetActive(false);
                }
                return;
            }

            float baseSpeed = locomotion.BaseMoveSpeed + stats.Raw.moveSpeedMod;
            float effectiveSpeed = baseSpeed * stats.SpeedMultiplier;
            bool penalized = stats.SpeedMultiplier < 1f;

            // D-9: 主表示は実効値。基礎値は副表示で、減っているときだけ見せて「一目でわかる」ようにする。
            if (speedText != null)
            {
                speedText.text = $"速度 {effectiveSpeed:0.0}";
                speedText.color = penalized ? warningColor : normalColor;
            }

            if (speedBaseText != null)
            {
                speedBaseText.gameObject.SetActive(penalized);
                if (penalized)
                {
                    speedBaseText.text = $"(基礎 {baseSpeed:0.0})";
                }
            }
        }

        private void RefreshCapabilities(CapabilityFlags rawCapabilities, CapabilityFlags activeCapabilities)
        {
            if (capabilityIconContainer == null || capabilityIconPrefab == null)
            {
                return;
            }

            // 一覧の対象は「一度でも付与されている」もの(raw)。そこから剥奪済みかどうかを
            // activeCapabilities と突き合わせて判定する。「そもそも付与されていない」ものは
            // raw に無いので一覧に出てこない ―― これが2状態を見た目で分ける前提になる(D-9)。
            IReadOnlyList<CapabilityIconSet.Entry> entries = capabilityIconSet != null
                ? capabilityIconSet.GetEntries(rawCapabilities)
                : Array.Empty<CapabilityIconSet.Entry>();

            EnsureIconPool(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                bool active = activeCapabilities.Has(entries[i].Flag);
                _capabilityIconPool[i].gameObject.SetActive(true);
                _capabilityIconPool[i].SetState(entries[i].Icon, active);
            }
            for (int i = entries.Count; i < _capabilityIconPool.Count; i++)
            {
                _capabilityIconPool[i].gameObject.SetActive(false);
            }
        }

        private void EnsureIconPool(int count)
        {
            while (_capabilityIconPool.Count < count)
            {
                _capabilityIconPool.Add(Instantiate(capabilityIconPrefab, capabilityIconContainer));
            }
        }

        private void RefreshIssues(LoadoutValidation validation)
        {
            if (issueListContainer == null || issueTextPrefab == null)
            {
                return;
            }

            IReadOnlyList<ValidationIssue> issues = validation != null
                ? validation.Issues
                : Array.Empty<ValidationIssue>();

            EnsureIssuePool(issues.Count);
            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                Text text = _issueTextPool[i];
                text.gameObject.SetActive(true);
                text.text = issue.Message;
                text.color = issue.Severity == ValidationSeverity.Error ? errorColor : warningColor;
            }
            for (int i = issues.Count; i < _issueTextPool.Count; i++)
            {
                _issueTextPool[i].gameObject.SetActive(false);
            }
        }

        private void EnsureIssuePool(int count)
        {
            while (_issueTextPool.Count < count)
            {
                _issueTextPool.Add(Instantiate(issueTextPrefab, issueListContainer));
            }
        }
    }
}
