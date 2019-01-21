namespace Umbraco.Courier.Contrib.Resolvers.PropertyDataResolvers
{
    public class UmbracoMultiUrlPickerPropertyDataResolver : MultiUrlPickerPropertyDataResolver
    {
        /// <summary>
        /// Alias of the editor this resolver should trigger on.
        /// </summary>
        public override string EditorAlias
        {
            get { return "Umbraco.MultiUrlPicker"; }
        }
    }
}