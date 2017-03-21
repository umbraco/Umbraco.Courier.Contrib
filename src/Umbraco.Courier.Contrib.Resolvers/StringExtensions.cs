namespace Umbraco.Courier.Contrib.Resolvers
{
    internal static class StringExtensions
    {
        //From: http://stackoverflow.com/a/21455488/5018
        public static string EscapeForJson(this string text)
        {
            var quoted = System.Web.Helpers.Json.Encode(text);
            return quoted.Substring(1, quoted.Length - 2);
        }

        //From: https://github.com/umbraco/Umbraco-CMS/blob/dev-v7/src/Umbraco.Core/StringExtensions.cs#L123
        public static bool DetectIsJson(this string input)
        {
            input = input.Trim();
            return (input.StartsWith("{") && input.EndsWith("}"))
                   || (input.StartsWith("[") && input.EndsWith("]"));
        }
    }
}
