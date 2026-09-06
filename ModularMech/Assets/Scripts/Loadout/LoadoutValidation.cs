using System.Collections.Generic;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// 1回の検証の結果。UI は <see cref="Issues"/> をそのまま列挙して表示できる。
    /// </summary>
    public sealed class LoadoutValidation
    {
        private static readonly ValidationIssue[] NoIssues = new ValidationIssue[0];

        /// <summary>問題なしを表す共有インスタンス。</summary>
        public static readonly LoadoutValidation Valid = new LoadoutValidation(NoIssues);

        public LoadoutValidation(IReadOnlyList<ValidationIssue> issues)
        {
            Issues = issues ?? NoIssues;

            // 判定は生成時に一度だけ行う。UI が毎フレーム参照しても走査が走らないようにするため。
            var deployable = true;
            var hasWarnings = false;
            for (int i = 0; i < Issues.Count; i++)
            {
                if (Issues[i].Severity == ValidationSeverity.Error) deployable = false;
                else hasWarnings = true;
            }

            IsDeployable = deployable;
            HasWarnings = hasWarnings;
        }

        public IReadOnlyList<ValidationIssue> Issues { get; }

        /// <summary>Error が 0 件なら出撃可能。警告(過積載・パワー不足)は出撃を妨げない。</summary>
        public bool IsDeployable { get; }

        public bool HasWarnings { get; }

        public bool HasIssues => Issues.Count > 0;
    }
}
