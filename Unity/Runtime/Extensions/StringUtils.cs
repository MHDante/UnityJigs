namespace UnityJigs.Extensions
{
    public static class StringUtils
    {
        public static string? NullIfWhitespace(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
