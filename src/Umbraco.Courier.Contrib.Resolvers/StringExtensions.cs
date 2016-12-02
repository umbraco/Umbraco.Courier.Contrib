namespace Umbraco.Courier.Contrib.Resolvers
{
    //From: http://stackoverflow.com/a/21455488/5018
    public static class StringExtensions
    {
        public static string EscapeForJson(this string text)
        {
            var quoted = System.Web.Helpers.Json.Encode(text);
            return quoted.Substring(1, quoted.Length - 2);
        }
    }
}
