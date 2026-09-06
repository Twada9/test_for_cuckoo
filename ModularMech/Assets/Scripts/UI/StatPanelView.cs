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
    /// <item>速度: ペナルティ適用後の実効値を主表示、基礎値を副表示(減っているときだけ)。
    /// スプリント倍率は主表示に含まれない(押している間だけ超える)ので、副表示に
    /// 「走行時 ×N」として出す(D-23)。</item>
    /// <item>重量: 総重量/上限。WeightCapacity&lt;=0(脚未装備等)は「—」であって「0%」ではない。</item>
    /// <item>電力: 出力/消費の順。比率(PowerRatio) 1.0 未満で色を変える。</item>
    /// <item>能力アイコン: 「そもそも付与されていない」(一覧に出ない)と
    /// 「付与されたがペナルティで剥奪された」(暗く表示)を区別する。</item>
    /// <item>警告行: ValidationIssue.Message を1件1行、Severity で色分け。
    /// セーブ/ロード経路の警告(<see cref="SetNotices"/>)も同じ警告行に出す(D-17)。</item>
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

        [Tooltip("副表示(スプリント)。「走行時 ×N」を出す。Run が無い/剥奪されているときは隠す。任意。")]
        [SerializeField] private Text speedRunText;

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

        /// <summary>セーブ/ロード経路の注意書き。Validation とは出所が違うので別に保持する(D-17)。</summary>
        private readonly List<string> _notices = new List<string>();

        /// <summary>最後に渡された検証結果。注意書きだけが更新されたときの再描画に使う。</summary>
        private LoadoutValidation _lastValidation;

        /// <summary>
        /// MechRuntime.LoadoutApplied のハンドラから呼ぶ想定。
        /// </summary>
        /// <param name="rawCapabilities">
        /// ペナルティ適用前の(本来付与されている)能力の合計。StatBlock.Capabilities はペナルティ
        /// 適用後の値しか持たないため、「剥奪された」表示のために呼び出し側(GarageScreen)で
        /// 別途集約して渡してもらう。
        /// </param>
        /// <param name="runSpeedRatio">
        /// スプリント倍率(<see cref="ModularMech.Mechs.MechLocomotionController.RunSpeedRatio"/>)。
        /// 副表示「走行時 ×N」に使う。倍率の出典はコントローラ1箇所なので(D-23)、
        /// このビューは既定値を持たない ―― 取得できないときは 0 を渡して副表示を消すこと。
        /// </param>
        public void Refresh(
            StatBlock stats,
            LoadoutValidation validation,
            ILocomotionProfileData locomotion,
            CapabilityFlags rawCapabilities,
            float runSpeedRatio)
        {
            _lastValidation = validation;

            EnsureFillBarsConfigured();
            RefreshWeight(stats);
            RefreshPower(stats);
            RefreshSpeed(stats, locomotion, runSpeedRatio);
            RefreshCapabilities(rawCapabilities, stats.Capabilities);
            RefreshIssues();
        }

        /// <summary>
        /// セーブ/ロード経路の注意書きを差し替える(D-17)。<see cref="LoadoutLoadResult.Warnings"/> /
        /// <see cref="LoadoutSaveResult.Warnings"/> をそのまま渡す想定。
        ///
        /// <para>
        /// これらは <see cref="LoadoutValidation"/> には入ってこない。Serializer は §7 どおり
        /// 「未知の ID のスロットを空にして警告を積む」だけで例外も Error も出さないため、
        /// UI がここで拾わないと、プレイヤーは保存したパーツが消えたことに気づけない。
        /// </para>
        /// <para>
        /// 空リスト(または null)を渡せば以前の注意書きは消える。表示は常に
        /// 「最後に行ったディスク操作の結果」を意味する。
        /// </para>
        /// </summary>
        public void SetNotices(IReadOnlyList<string> notices)
        {
            _notices.Clear();

            if (notices != null)
            {
                // 呼び出し側のリストを後から書き換えられても表示が化けないよう、中身を写して持つ。
                for (int i = 0; i < notices.Count; i++)
                {
                    if (!string.IsNullOrEmpty(notices[i]))
                    {
                        _notices.Add(notices[i]);
                    }
                }
            }

            RefreshIssues();
        }

        private bool _fillBarsConfigured;

        /// <summary>
        /// <see cref="Image.fillAmount"/> は <see cref="Image.type"/> が <c>Filled</c> でないと
        /// 描画に反映されない(代入自体は例外も警告も出ずに成立する)。シーン構築側
        /// (<c>GarageSceneBuilder</c>)は Filled で生成するが、差し替えプレハブ経由で配線された
        /// 場合にも壊れないよう、ここでも一度だけ強制する(1回で十分。毎フレーム呼ばれる経路ではない)。
        /// </summary>
        private void EnsureFillBarsConfigured()
        {
            if (_fillBarsConfigured)
            {
                return;
            }
            _fillBarsConfigured = true;

            ConfigureFillBar(weightBarFill);
            ConfigureFillBar(weightOverflowBarFill);
            ConfigureFillBar(powerBarFill);
        }

        private static void ConfigureFillBar(Image image)
        {
            if (image == null)
            {
                return;
            }
            if (image.type != Image.Type.Filled)
            {
                image.type = Image.Type.Filled;
            }
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
                // バーは「出力に対してどれだけ消費しているか」。出力 0 で消費があるときは比が定義できないが、
                // それは「消費していない」ではなく「まったく足りていない」状態なので満杯にする。
                // 空バーにすると無負荷に見えて、電源が無い構成の異常さが読み取れない(D-9)。
                float fill;
                if (stats.TotalPowerOutput > 0f)
                {
                    fill = Mathf.Clamp01(stats.TotalPowerDraw / stats.TotalPowerOutput);
                }
                else
                {
                    fill = stats.TotalPowerDraw > 0f ? 1f : 0f;
                }

                powerBarFill.fillAmount = fill;
                powerBarFill.color = stats.IsUnderpowered ? warningColor : normalColor;
            }
        }

        private void RefreshSpeed(StatBlock stats, ILocomotionProfileData locomotion, float runSpeedRatio)
        {
            if (speedText == null && speedBaseText == null && speedRunText == null)
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
                if (speedRunText != null)
                {
                    speedRunText.gameObject.SetActive(false);
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

            // スプリント中の実速度は主表示を超える。D-9 の「表示と実効値が一致する」を保つため、
            // 倍率と到達速度を副表示に出す(D-23)。Run が無い / ペナルティで剥奪されている構成では
            // スプリント入力そのものが落とされる(MechLocomotionController.ApplyCapabilityLocks)ので、
            // 出せば嘘になる。倍率が 1 以下(未取得を表す 0 を含む)のときも出さない。
            if (speedRunText != null)
            {
                bool canRun = stats.Capabilities.Has(CapabilityFlags.Run) && runSpeedRatio > 1f;
                speedRunText.gameObject.SetActive(canRun);
                if (canRun)
                {
                    speedRunText.text = $"走行時 ×{runSpeedRatio:0.0} ({effectiveSpeed * runSpeedRatio:0.0})";
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
                _capabilityIconPool[i].SetState(entries[i].Icon, entries[i].DisplayName, active);
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

        /// <summary>
        /// 警告行を描き直す。セーブ/ロード由来の注意書きを先に、検証結果をその後に並べる。
        /// 注意書きを先頭に置くのは、「保存したパーツが消えている」ことが、その結果として出る
        /// 「Torso がありません」等より先に読まれるべき情報だから(D-17)。
        /// </summary>
        private void RefreshIssues()
        {
            if (issueListContainer == null || issueTextPrefab == null)
            {
                return;
            }

            IReadOnlyList<ValidationIssue> issues = _lastValidation != null
                ? _lastValidation.Issues
                : Array.Empty<ValidationIssue>();

            int total = _notices.Count + issues.Count;
            EnsureIssuePool(total);

            for (int i = 0; i < _notices.Count; i++)
            {
                Text text = _issueTextPool[i];
                text.gameObject.SetActive(true);
                text.text = _notices[i];

                // 保存データ由来の注意書きは Severity を持たない。出撃を止める性質のものではないので
                // 一律 Warning 色にする(出撃可否は LoadoutValidation.IsDeployable だけが決める)。
                text.color = warningColor;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                Text text = _issueTextPool[_notices.Count + i];
                text.gameObject.SetActive(true);
                text.text = issue.Message;
                text.color = issue.Severity == ValidationSeverity.Error ? errorColor : warningColor;
            }

            for (int i = total; i < _issueTextPool.Count; i++)
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
