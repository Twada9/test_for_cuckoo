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
    /// 総重量/重量上限、消費電力/出力、速度、能力アイコンを表示する。過積載・パワー不足は
    /// 色付きで警告表示し、LoadoutValidation.Issues をそのまま並べる。
    /// MechRuntime.LoadoutApplied を受けて <see cref="Refresh"/> が呼ばれたときだけ更新し、
    /// 毎フレームは何もしない。
    /// </summary>
    public sealed class StatPanelView : MonoBehaviour
    {
        [Header("数値表示")]
        [SerializeField] private Text weightText;
        [SerializeField] private Text powerText;
        [SerializeField] private Text speedText;

        [Header("バー(任意。使わないなら null のままでよい)")]
        [SerializeField] private Image weightBarFill;
        [SerializeField] private Image powerBarFill;

        [Header("色")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warningColor = new Color(1f, 0.65f, 0f);
        [SerializeField] private Color errorColor = Color.red;

        [Header("能力アイコン")]
        [SerializeField] private CapabilityIconSet capabilityIconSet;
        [SerializeField] private Transform capabilityIconContainer;
        [SerializeField] private Image capabilityIconPrefab;

        [Header("警告/エラー一覧(LoadoutValidation.Issues)")]
        [SerializeField] private Transform issueListContainer;
        [SerializeField] private Text issueTextPrefab;

        private readonly List<Image> _capabilityIconPool = new List<Image>();
        private readonly List<Text> _issueTextPool = new List<Text>();

        /// <summary>MechRuntime.LoadoutApplied のハンドラから呼ぶ想定。</summary>
        public void Refresh(StatBlock stats, LoadoutValidation validation, ILocomotionProfileData locomotion)
        {
            RefreshWeight(stats);
            RefreshPower(stats);
            RefreshSpeed(stats, locomotion);
            RefreshCapabilities(stats.Capabilities);
            RefreshIssues(validation);
        }

        private void RefreshWeight(StatBlock stats)
        {
            if (weightText != null)
            {
                weightText.text = $"重量 {stats.TotalWeight:0.0} / {stats.WeightCapacity:0.0}";
                weightText.color = stats.IsOverweight ? errorColor : normalColor;
            }

            if (weightBarFill != null)
            {
                float ratio = stats.WeightCapacity > 0f ? stats.TotalWeight / stats.WeightCapacity : 0f;
                weightBarFill.fillAmount = Mathf.Clamp01(ratio);
                weightBarFill.color = stats.IsOverweight ? errorColor : normalColor;
            }
        }

        private void RefreshPower(StatBlock stats)
        {
            if (powerText != null)
            {
                powerText.text = $"電力 {stats.TotalPowerDraw:0.0} / {stats.TotalPowerOutput:0.0}";
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
            if (speedText == null)
            {
                return;
            }

            if (locomotion == null)
            {
                speedText.text = "速度 -";
                speedText.color = normalColor;
                return;
            }

            float speed = (locomotion.BaseMoveSpeed + stats.Raw.moveSpeedMod) * stats.SpeedMultiplier;
            speedText.text = $"速度 {speed:0.0}";
            speedText.color = stats.SpeedMultiplier < 1f ? warningColor : normalColor;
        }

        private void RefreshCapabilities(CapabilityFlags capabilities)
        {
            if (capabilityIconContainer == null || capabilityIconPrefab == null)
            {
                return;
            }

            IReadOnlyList<CapabilityIconSet.Entry> entries = capabilityIconSet != null
                ? capabilityIconSet.GetEntries(capabilities)
                : Array.Empty<CapabilityIconSet.Entry>();

            EnsureIconPool(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                _capabilityIconPool[i].gameObject.SetActive(true);
                _capabilityIconPool[i].sprite = entries[i].Icon;
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
