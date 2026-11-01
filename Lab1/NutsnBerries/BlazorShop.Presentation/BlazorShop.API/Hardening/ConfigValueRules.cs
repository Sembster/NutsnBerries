namespace BlazorShop.API.Hardening
{
    /// <summary>
    /// Shared checks for configuration values. Committed config files use <c>&lt;...&gt;</c> placeholders for
    /// secrets (for example <c>&lt;set-in-user-secrets&gt;</c>); such a value must never be treated as real.
    /// </summary>
    internal static class ConfigValueRules
    {
        public static bool IsPlaceholder(string? value)
        {
            var trimmed = value?.Trim();
            return trimmed is { Length: >= 2 } && trimmed[0] == '<' && trimmed[^1] == '>';
        }

        public static bool IsUsable(string? value) => !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value);
    }
}
