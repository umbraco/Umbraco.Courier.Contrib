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

namespace Umbraco.Courier.Contrib.Resolvers.PropertyDataResolvers
{
    /// <summary>
    /// An abstract Property Data Resolver for Inner Content by Matt Brailsford & Lee Kelleher.
    /// </summary>
    public abstract class InnerContentPropertyDataResolver : PropertyDataResolverProvider
    {
        /// <summary>
        /// This is triggered when the property is about to be extracted (Courier -> Umbraco).
        /// </summary>
        /// <param name="item">Item being extracted</param>
        /// <param name="propertyData">Inner Content property being extracted</param>
        public override void ExtractingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Extracting);
        }

        /// <summary>
        /// This is triggered when the property is about to be packaged (Umbraco -> Courier).
        /// </summary>
        /// <param name="item">The DataType item being packaged</param>
        public override void PackagingDataType(DataType item)
        {
            AddDataTypeDependencies(item);
        }

        /// <summary>
        /// This is triggered when the property is about to be packaged (Umbraco -> Courier).
        /// </summary>
        /// <param name="item">Item being packaged</param>
        /// <param name="propertyData">Inner Content property being packaged</param>
        public override void PackagingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Packaging);
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
                var alias = contentType["icContentTypeAlias"];
                if (alias == null)
                    continue;

                var documentType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(alias.ToString(), ItemProviderIds.documentTypeItemProviderGuid));
                if (documentType == null)
                    continue;

                item.Dependencies.Add(documentType.UniqueId.ToString(), ItemProviderIds.documentTypeItemProviderGuid);
            }
        }

        private void ProcessPropertyData(Item item, ContentProperty propertyData, Action action)
        {
            // add a dependency to the dataType if packaging
            if (action == Action.Packaging)
                item.Dependencies.Add(propertyData.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);

            // get the ItemProvider for the ResolutionManager
            var propertyItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

            // deserialize the Inner Content value into an array of Inner Content items
            var innerContentItems = JsonConvert.DeserializeObject<JArray>(propertyData.Value.ToString());

            ProcessItems(item, innerContentItems, propertyItemProvider, action);

            // serialize the whole Inner Content property back to JSON and save the value on the property data
            propertyData.Value = JsonConvert.SerializeObject(innerContentItems);
        }

        private void ProcessItems(Item item, JArray innerContentItems, ItemProvider propertyItemProvider, Action action)
        {
            // loop through all the Inner Content items
            if (innerContentItems != null && innerContentItems.Any())
            {
                foreach (var innerContentItem in innerContentItems)
                {
                    // get the document type for the item, if it can't be found, skip to the next item
                    var docTypeAlias = innerContentItem["icContentTypeAlias"];
                    if (docTypeAlias == null)
                        continue;

                    var docType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(docTypeAlias.ToString(), ItemProviderIds.documentTypeItemProviderGuid));
                    if (docType == null)
                        continue;

                    // get the properties available on the document type
                    var propertyTypes = docType.Properties;

                    // add in properties from all composition document types, as these are not located on the document type itself
                    foreach (var masterDocTypeAlias in docType.MasterDocumentTypes)
                    {
                        var masterDocType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(masterDocTypeAlias, ItemProviderIds.documentTypeItemProviderGuid));
                        if (masterDocType != null)
                            propertyTypes.AddRange(masterDocType.Properties);
                    }

                    // run through all properties, creating pseudo items and sending them through the resolvers
                    foreach (var propertyType in propertyTypes)
                    {
                        ProcessItemPropertyData(item, propertyType, innerContentItem, propertyItemProvider, action);
                    }
                }
            }
        }

        private void ProcessItemPropertyData(Item item, ContentTypeProperty propertyType, JToken innerContentItem, ItemProvider propertyItemProvider, Action action)
        {
            var value = innerContentItem[propertyType.Alias];
            if (value != null)
            {
                var dataType = ExecutionContext.DatabasePersistence.RetrieveItem<DataType>(new ItemIdentifier(propertyType.DataTypeDefinitionId.ToString(), ItemProviderIds.dataTypeItemProviderGuid));

                // create a 'fake' item for Courier to process
                var pseudoPropertyDataItem = new ContentPropertyData
                {
                    ItemId = item.ItemId,
                    Name = string.Format("{0} [{1}: Inner {2} ({3})]", item.Name, this.EditorAlias, dataType.PropertyEditorAlias, propertyType.Alias),
                    Data = new List<ContentProperty>
                    {
                        new ContentProperty
                        {
                            Alias = propertyType.Alias,
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
                        // run the 'fake' item through Courier's data resolvers
                        ResolutionManager.Instance.PackagingItem(pseudoPropertyDataItem, propertyItemProvider);
                    }
                    catch (Exception ex)
                    {
                        CourierLogHelper.Error<InnerContentPropertyDataResolver>(string.Concat("Error packaging data value: ", pseudoPropertyDataItem.Name), ex);
                    }
                }
                else if (action == Action.Extracting)
                {
                    try
                    {
                        // run the 'fake' item through Courier's data resolvers
                        ResolutionManager.Instance.ExtractingItem(pseudoPropertyDataItem, propertyItemProvider);
                    }
                    catch (Exception ex)
                    {
                        CourierLogHelper.Error<InnerContentPropertyDataResolver>(string.Concat("Error extracting data value: ", pseudoPropertyDataItem.Name), ex);
                    }
                }

                // pass up the dependencies and resources
                item.Dependencies.AddRange(pseudoPropertyDataItem.Dependencies);
                item.Resources.AddRange(pseudoPropertyDataItem.Resources);

                if (pseudoPropertyDataItem.Data != null && pseudoPropertyDataItem.Data.Any())
                {
                    // get the first (and only) property of the pseudo item created above
                    var firstProperty = pseudoPropertyDataItem.Data.FirstOrDefault();
                    if (firstProperty != null)
                    {
                        // serialize the value of the property
                        var serializedValue = firstProperty.Value as string ?? JsonConvert.SerializeObject(firstProperty.Value);

                        // replace the values on the Inner Content item property with the resolved values
                        innerContentItem[propertyType.Alias] = new JValue(serializedValue);

                        // if packaging - add a dependency for the property's data-type
                        if (action == Action.Packaging)
                            item.Dependencies.Add(firstProperty.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                    }
                }
            }
        }
    }
}