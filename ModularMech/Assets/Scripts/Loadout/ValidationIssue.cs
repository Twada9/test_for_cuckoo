using ModularMech.Data;

namespace ModularMech.Loadouts
{
    /// <summary>
    /// 検証で見つかった1件の問題。<see cref="Message"/> は UI にそのまま出せる日本語文にしてある。
    /// </summary>
    public readonly struct ValidationIssue
    {
        private ValidationIssue(ValidationCode code, ValidationSeverity severity, PartSlot slot, bool hasSlot, string message)
        {
            Code = code;
            Severity = severity;
            Slot = slot;
            HasSlot = hasSlot;
            Message = message;
        }

        public ValidationCode Code { get; }
        public ValidationSeverity Severity { get; }

        /// <summary>問題のあるスロット。<see cref="HasSlot"/> が false のときは意味を持たない。</summary>
        public PartSlot Slot { get; }

        /// <summary>機体全体に関わる問題(電力・重量)は特定のスロットに紐づかないため false。</summary>
        public bool HasSlot { get; }

        /// <summary>UI 表示用の説明。超過量などの数値を含む。</summary>
        public string Message { get; }

        public bool IsError => Severity == ValidationSeverity.Error;

        public static ValidationIssue Error(ValidationCode code, string message)
        {
            return new ValidationIssue(code, ValidationSeverity.Error, default, false, message);
        }

        public static ValidationIssue Error(ValidationCode code, PartSlot slot, string message)
        {
            return new ValidationIssue(code, ValidationSeverity.Error, slot, true, message);
        }

        public static ValidationIssue Warning(ValidationCode code, string message)
        {
            return new ValidationIssue(code, ValidationSeverity.Warning, default, false, message);
        }

        public static ValidationIssue Warning(ValidationCode code, PartSlot slot, string message)
        {
            return new ValidationIssue(code, ValidationSeverity.Warning, slot, true, message);
        }

        public override string ToString()
        {
            var head = Severity == ValidationSeverity.Error ? "Error" : "Warning";
            return HasSlot ? $"[{head}/{Code}/{Slot}] {Message}" : $"[{head}/{Code}] {Message}";
        }
    }
}
