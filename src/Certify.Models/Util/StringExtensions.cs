namespace Certify
{
    public static class StringExtensions
    {
        /// <summary>
        /// Choose between two string values, use second value if first is null, blank or whitespace, this is useful where model properties have moved to a blank default instead of null but code uses null coalescing (??)
        /// Note this doesn't work if chaining nulls such as a?.b?.stringValue.WithDefault("example)
        /// </summary>
        /// <param name="s"></param>
        /// <param name="subtituteValue"></param>
        /// <returns></returns>
        public static string? WithDefault(this string? s, string subtituteValue)
        {
            return string.IsNullOrWhiteSpace(s) ? subtituteValue : s;
        }

        /// <summary>
        /// Normalise string value to null if it's null, blank or whitespace. This is useful when using null coalesce ?? to provide a default value
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string? AsNullWhenBlank(this string? s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
    }
}
