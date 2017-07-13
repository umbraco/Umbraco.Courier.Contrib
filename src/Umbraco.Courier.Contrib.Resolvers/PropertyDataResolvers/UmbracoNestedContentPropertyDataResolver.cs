namespace Umbraco.Courier.Contrib.Resolvers.PropertyDataResolvers
{
    /// <summary>
    /// Nested Content Property Data Resolver for Nested Content (Umbraco 7.7+) by Matt Brailsford & Lee Kelleher.
    /// This resolver is reusing the NestedContentPropertyDataResolver.
    /// By overriding the EditorAlias, we can reuse it for Nested Content which is now included in Umbraco 7.7+.
    /// </summary>
    public class UmbracoNestedContentPropertyDataResolver : NestedContentPropertyDataResolver
    {
        /// <summary>
        /// Alias of the editor this resolver should trigger on.
        /// </summary>
        public override string EditorAlias
        {
            get { return "Umbraco.NestedContent"; }
        }
    }
}