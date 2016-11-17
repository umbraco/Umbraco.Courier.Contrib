using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.Logging;
using Umbraco.Courier.Core.ProviderModel;
using Umbraco.Courier.DataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Umbraco.Courier.Contrib.Resolvers.NestedContent
{
    /// <summary>
    /// Nested Content Property Data Resolver for Nested Content by Matt Brailsford & Lee Kelleher.
    /// </summary>
    public class NestedContentPropertyDataResolver : PropertyDataResolverProvider
    {
        /// <summary>
        /// Alias of the editor this resolver should trigger on.
        /// </summary>
        public override string EditorAlias
        {
            get { return "Our.Umbraco.NestedContent"; }
        }

        /// <summary>
        /// This is triggered when the property is about to be packaged (Umbraco -> Courier).
        /// </summary>
        /// <param name="item">Item being packaged</param>
        /// <param name="propertyData">Nested Content property being packaged</param>
        public override void PackagingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Packaging);
        }

        /// <summary>
        /// This is triggered when the property is about to be extracted (Courier -> Umbraco).
        /// </summary>
        /// <param name="item">Item being extracted</param>
        /// <param name="propertyData">Nested Content property being extracted</param>
        public override void ExtractingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Extracting);
        }

        public override void PackagingDataType(DataType item)
        {
            AddDataTypeDependencies(item);
        }

        private void AddDataTypeDependencies(DataType item)
        {
            if (item == null || item.Prevalues == null || item.Prevalues.Count == 0)
                return;

            var json = item.Prevalues.FirstOrDefault(x => x.Alias.InvariantEquals("contentTypes"));
            if (json == null)
                return;

            var contentTypes = JsonConvert.DeserializeObject<JArray>(json.Value);
            if (contentTypes == null)
                return;

            foreach (var contentType in contentTypes)
            {
                var alias = contentType["ncAlias"];
                if (alias == null)
                    continue;

                var documentType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(alias.ToString(), ItemProviderIds.documentTypeItemProviderGuid));
                if (documentType == null)
                    continue;

                item.Dependencies.Add(documentType.UniqueId.ToString(), ItemProviderIds.documentTypeItemProviderGuid);
            }
        }

        /// <summary>
        /// Processes the property data.
        /// This method is used both for packaging and extracting property data.
        /// We want to deserialize the property data and then run it through the ResolutionManager for either packaging or extracting.
        /// This is done by creating a pseudo item immitating a property data item and having the ResolutionManager use its normal resolvers for resolving and finding dependencies.
        /// If we are packaging we also add any found dependencies and resources to the item which the property data belongs to.
        /// </summary>
        /// <param name="item">Item being handled</param>
        /// <param name="propertyData">Nested Content property being handled</param>
        /// <param name="action">Indicates if we are packaging or extracting the item/property</param>
        private void ProcessPropertyData(Item item, ContentProperty propertyData, Action action)
        {
            // add a dependency to the dataType if packaging
            if (action == Action.Packaging)
                item.Dependencies.Add(propertyData.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);

            // deserialize the Nested Content value into an array of Nested Content items
            var nestedContentItems = JsonConvert.DeserializeObject<JArray>(propertyData.Value.ToString());

            // get the ItemProvider for the ResolutionManager
            var propertyDataItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

            // loop through all the Nested Content items
            foreach (var nestedContentItem in nestedContentItems)
            {
                // get the document type for the item, if it can't be found, skip to the next item
                var documentTypeAlias = nestedContentItem["ncContentTypeAlias"];
                if (documentTypeAlias == null)
                    continue;
                var documentType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(documentTypeAlias.ToString(), ItemProviderIds.documentTypeItemProviderGuid));
                if (documentType == null)
                    continue;

                // get the properties available on the document type
                var properties = documentType.Properties;

                // add in properties from all composition document types, as these are not located on the document type itself
                foreach (var masterDocumentTypeAlias in documentType.MasterDocumentTypes)
                {
                    var masterDocumentType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(masterDocumentTypeAlias, ItemProviderIds.documentTypeItemProviderGuid));
                    if (masterDocumentType != null)
                        properties.AddRange(masterDocumentType.Properties);
                }

                // run through all properties, creating pseudo items and sending them through the resolvers
                foreach (var property in properties)
                {
                    var value = nestedContentItem[property.Alias];
                    if (value != null)
                    {
                        var dataType = ExecutionContext.DatabasePersistence.RetrieveItem<DataType>(new ItemIdentifier(property.DataTypeDefinitionId.ToString(), ItemProviderIds.dataTypeItemProviderGuid));

                        var pseudoPropertyDataItem = new ContentPropertyData
                        {
                            ItemId = item.ItemId,
                            Name = string.Format("{0} [{1}: Nested {2} ({3})]", item.Name, propertyData.PropertyEditorAlias, dataType.PropertyEditorAlias, property.Alias),
                            Data = new List<ContentProperty>
                            {
                                new ContentProperty
                                {
                                    Alias = property.Alias,
                                    DataType = dataType.UniqueID,
                                    PropertyEditorAlias = dataType.PropertyEditorAlias,
                                    Value = value.ToString()
                                }
                            }
                        };
                        if (action == Action.Packaging)
                        {
                            try
                            {
                                // run the resolvers (convert Ids/integers into UniqueIds/guids)
                                ResolutionManager.Instance.PackagingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                            }
                            catch (Exception ex)
                            {
                                CourierLogHelper.Error<NestedContentPropertyDataResolver>(string.Concat("Error packaging data value: ", pseudoPropertyDataItem.Name), ex);
                            }
                            // add in dependencies when packaging
                            item.Dependencies.AddRange(pseudoPropertyDataItem.Dependencies);
                            item.Resources.AddRange(pseudoPropertyDataItem.Resources);
                        }
                        else
                        {
                            try
                            {
                                // run the resolvers (convert UniqueIds/guids back to Ids/integers)
                                ResolutionManager.Instance.ExtractingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                            }
                            catch (Exception ex)
                            {
                                CourierLogHelper.Error<NestedContentPropertyDataResolver>(string.Concat("Error extracting data value: ", pseudoPropertyDataItem.Name), ex);
                            }
                        }

                        if (pseudoPropertyDataItem.Data != null && pseudoPropertyDataItem.Data.Any())
                        {
                            // get the first (and only) property of the pseudo item created above
                            var firstProperty = pseudoPropertyDataItem.Data.FirstOrDefault();
                            if (firstProperty != null)
                            {
                                // serialize the value of the property
                                var serializedValue = firstProperty.Value as string ?? JsonConvert.SerializeObject(firstProperty.Value);

                                // replace the values on the Nested Content item property with the resolved values
                                nestedContentItem[property.Alias] = new JValue(serializedValue);

                                // if packaging - add a dependency for the property's data-type
                                if (action == Action.Packaging)
                                    item.Dependencies.Add(firstProperty.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                            }
                        }
                    }
                }
            }
            // serialize the whole vorto property back to json and save the value on the property data
            propertyData.Value = JsonConvert.SerializeObject(nestedContentItems);
        }
    }
}