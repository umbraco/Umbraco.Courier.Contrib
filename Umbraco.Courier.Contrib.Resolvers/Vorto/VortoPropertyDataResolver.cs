using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.ProviderModel;
using Umbraco.Courier.DataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Umbraco.Courier.Contrib.Resolvers.Vorto
{
    /// <summary>
    /// Vorto Property Data Resolver for Vorto by Matt Brailsford.
    /// </summary>
    public class VortoPropertyDataResolver : PropertyDataResolverProvider
    {
        /// <summary>
        /// Alias of the editor this resolver should trigger on.
        /// </summary>
        public override string EditorAlias
        {
            get { return "Our.Umbraco.Vorto"; }
        }

        /// <summary>
        /// Indicates if we are packaging or extracting.
        /// </summary>
        private enum Action
        {
            Packaging,
            Extracting
        }

        /// <summary>
        /// This is triggered when the property is about to be packaged (Umbraco -> Courier).
        /// </summary>
        /// <param name="item">Item being packaged</param>
        /// <param name="propertyData">Vorto property being packaged</param>
        public override void PackagingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Packaging);
            base.PackagingProperty(item, propertyData);
        }

        /// <summary>
        /// This is triggered when the property is about to be extracted (Courier -> Umbraco).
        /// </summary>
        /// <param name="item">Item being extracted</param>
        /// <param name="propertyData">Vorto property being extracted</param>
        public override void ExtractingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Extracting);
            base.ExtractingProperty(item, propertyData);
        }
        
        /// <summary>
        /// Processes the property data.
        /// This method is used both for packaging and extracting property data.
        /// We want to deserialize the property data and then run it through the ResolutionManager for either packaging or extracting.
        /// This is done by creating a pseudo item immitating a property data item and having the ResolutionManager use its normal resolvers for resolving and finding dependencies.
        /// If we are packaging we also add any found dependencies and resources to the item which the property data belongs to.
        /// </summary>
        /// <param name="item">Item being handled</param>
        /// <param name="propertyData">Vorto property being handled</param>
        /// <param name="action">Indicates if we are packaging or extracting the item/property</param>
        private void ProcessPropertyData(Item item, ContentProperty propertyData, Action action)
        {
            var vortoProperty = JsonConvert.DeserializeObject<VortoPropertyData>(propertyData.Value.ToString());

            if (vortoProperty.Values != null)
            {
                // deserialize all the vorto data and find the inner datatypes
                var dataTypeService = ApplicationContext.Current.Services.DataTypeService;
                var dataType = dataTypeService.GetDataTypeDefinitionById(propertyData.DataType);
                var vortoDataTypePrevalueJson = dataTypeService.GetPreValuesCollectionByDataTypeId(dataType.Id).FormatAsDictionary().FirstOrDefault(x => x.Key == "dataType").Value.Value;
                var vortoDataTypePrevalue = JsonConvert.DeserializeObject<VortoDatatypePrevalue>(vortoDataTypePrevalueJson);

                // get the ItemProvider for the ResolutionManager
                var propertyDataItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

                // create object to store resolved values
                var resolvedValues = new JObject();

                // run through all nested values, creating pseudo items and sending them through the resolvers
                foreach (var set in vortoProperty.Values)
                {
                    var language = set.Key;
                    var value = set.Value.ToString();

                    var pseudoPropertyDataItem = new ContentPropertyData
                    {
                        ItemId = item.ItemId,
                        Name = string.Format("{0}: (PropertyAlias: {1}, Language: {2})", item.Name, propertyData.Alias, language),
                        Data = new List<ContentProperty>
                        {
                            new ContentProperty
                            {
                                Alias = propertyData.Alias,
                                DataType = vortoDataTypePrevalue.Guid,
                                PropertyEditorAlias = vortoDataTypePrevalue.PropertyEditorAlias,
                                Value = value
                            }
                        }
                    };
                    if (action == Action.Packaging)
                    {
                        // run the resolvers (convert Ids/integers into UniqueIds/guids)
                        ResolutionManager.Instance.PackagingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                        // add in dependencies when packaging
                        item.Dependencies.AddRange(pseudoPropertyDataItem.Dependencies);
                        item.Resources.AddRange(pseudoPropertyDataItem.Resources);
                    }
                    else
                    {
                        // run the resolvers (convert UniqueIds/guids back to Ids/integers)
                        ResolutionManager.Instance.ExtractingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                    }
                    // add the resolved values to be replaced
                    resolvedValues.Add(new JProperty(language, pseudoPropertyDataItem.Data.FirstOrDefault().IfNotNull(x => x.Value)));
                }
                // replace the values on the property with the resolved values
                vortoProperty.Values = resolvedValues;
                // serialize the whole vorto property back to json and save the value on the property data
                propertyData.Value = JsonConvert.SerializeObject(vortoProperty);
            }
        }

        /// <summary>
        /// Used to deserialize a Vorto property
        /// </summary>
        internal class VortoPropertyData
        {
            [JsonProperty("values")]
            public JObject Values { get; set; }

            [JsonProperty("dtdGuid")]
            public Guid DtdGuid { get; set; }
        }

        /// <summary>
        /// Used to deserialize a Vorto DataType Prevalue
        /// </summary>
        internal class VortoDatatypePrevalue
        {
            [JsonProperty("guid")]
            public Guid Guid { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("propertyEditorAlias")]
            public string PropertyEditorAlias { get; set; }
        }
    }
}