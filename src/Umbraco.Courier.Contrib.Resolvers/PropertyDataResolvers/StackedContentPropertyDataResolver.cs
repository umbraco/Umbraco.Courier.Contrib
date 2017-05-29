namespace Umbraco.Courier.Contrib.Resolvers.PropertyDataResolvers
{
    /// <summary>
    /// Stacked Content Property Data Resolver for Stacked Content by Matt Brailsford & Lee Kelleher.
    /// </summary>
    public class StackedContentPropertyDataResolver : InnerContentPropertyDataResolver
    {
        /// <summary>
        /// Alias of the editor this resolver should trigger on.
        /// </summary>
        public override string EditorAlias
        {
            get
            {
                return "Our.Umbraco.StackedContent";
            }
        }
    }
}