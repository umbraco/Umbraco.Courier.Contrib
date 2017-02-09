using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.Logging;
using Umbraco.Courier.Core.ProviderModel;
using Umbraco.Courier.DataResolvers.PropertyDataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Umbraco.Courier.Contrib.Resolvers.GridCellDataResolvers
{
    /// <summary>
    /// DocTypeGridEditor Grid Cell Resolver for DTGE by Matt Brailsford & Lee Kelleher.
    /// </summary>
    public class DocTypeGridEditorGridCellResolver : GridCellResolverProvider
    {
        public override bool ShouldRun(string view, GridValueControlModel cell)
        {
            try
            {
                if (cell == null || cell.Value == null)
                    return false;

                if (cell.Value is JValue)
                    return false;

                return cell.Value["dtgeContentTypeAlias"] != null && cell.Value["value"] != null;
            }
            catch (Exception ex)
            {
                CourierLogHelper.Error<DocTypeGridEditorGridCellResolver>("Error reading grid cell value: ", ex);
                return false;
            }
        }

        public override void PackagingCell(Item item, ContentProperty propertyData, GridValueControlModel cell)
        {
            ProcessCell(item, propertyData, cell, Action.Packaging);
        }

        public override void ExtractingCell(Item item, ContentProperty propertyData, GridValueControlModel cell)
        {
            ProcessCell(item, propertyData, cell, Action.Extracting);
        }

        private void ProcessCell(Item item, ContentProperty propertyData, GridValueControlModel cell, Action direction)
        {
            var documentTypeAlias = cell.Value["dtgeContentTypeAlias"].ToString();
            if (string.IsNullOrWhiteSpace(documentTypeAlias))
                return;
            var documentType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(documentTypeAlias, ItemProviderIds.documentTypeItemProviderGuid));

            var cellValueJson = cell.Value["value"].ToString();
            if (string.IsNullOrWhiteSpace(cellValueJson))
                return;

            var cellValue = JsonConvert.DeserializeObject(cellValueJson);
            if (!(cellValue is JObject))
                return;

            var propertyValues = ((JObject)cellValue).ToObject<Dictionary<string, object>>();
            
            //this editor can contain references to doctypes that no longer exist
            //then it would be pointless to iterate over properties, they can't be set anyway
            if (documentType != null)
            {
                if (direction == Action.Packaging)
                {
                    item.Dependencies.Add(documentType.UniqueId.ToString(), ItemProviderIds.documentTypeItemProviderGuid);
                }

                // get the ItemProvider for the ResolutionManager
                var propertyDataItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

                var properties = documentType.Properties;

                // check for compositions
                foreach (var masterDocumentTypeAlias in documentType.MasterDocumentTypes)
                {
                    var masterDocumentType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(masterDocumentTypeAlias, ItemProviderIds.documentTypeItemProviderGuid));
                    if (masterDocumentType != null)
                        properties.AddRange(masterDocumentType.Properties);
                }

                foreach (var property in properties)
                {
                    object value = null;
                    if (!propertyValues.TryGetValue(property.Alias, out value) || value == null)
                        continue;

                    var datatype = ExecutionContext.DatabasePersistence.RetrieveItem<DataType>(new ItemIdentifier(property.DataTypeDefinitionId.ToString(), ItemProviderIds.dataTypeItemProviderGuid));

                    // create a pseudo item for sending through resolvers
                    var pseudoPropertyDataItem = new ContentPropertyData
                    {
                        ItemId = item.ItemId,
                        Name = string.Format("{0} [{1}: Nested {2} ({3})]", item.Name, EditorAlias, datatype.PropertyEditorAlias, property.Alias),
                        Data = new List<ContentProperty>
                        {
                            new ContentProperty
                            {
                                Alias = property.Alias,
                                DataType = datatype.UniqueID,
                                PropertyEditorAlias = datatype.PropertyEditorAlias,
                                Value = value
                            }
                        }
                    };

                    if (direction == Action.Packaging)
                    {
                        try
                        {
                            // run the resolvers (convert Ids/integers into UniqueIds/guids)
                            ResolutionManager.Instance.PackagingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                        }
                        catch (Exception ex)
                        {
                            CourierLogHelper.Error<DocTypeGridEditorGridCellResolver>(string.Concat("Error packaging data value: ", pseudoPropertyDataItem.Name), ex);
                        }
                        // add in dependencies when packaging
                        item.Dependencies.AddRange(pseudoPropertyDataItem.Dependencies);
                        item.Resources.AddRange(pseudoPropertyDataItem.Resources);
                    }
                    else if (direction == Action.Extracting)
                    {
                        try
                        {
                            // run the resolvers (convert UniqueIds/guids back to Ids/integers)
                            ResolutionManager.Instance.ExtractingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                        }
                        catch (Exception ex)
                        {
                            CourierLogHelper.Error<DocTypeGridEditorGridCellResolver>(
                                string.Concat("Error extracting data value: ", pseudoPropertyDataItem.Name), ex);
                        }
                    }

                    if (pseudoPropertyDataItem.Data != null && pseudoPropertyDataItem.Data.Any())
                    {
                        // get the first (and only) property of the pseudo item created above
                        var firstProperty = pseudoPropertyDataItem.Data.FirstOrDefault();
                        if (firstProperty != null)
                        {
                            // replace the property value with the resolved value
                            propertyValues[property.Alias] = firstProperty.Value;

                            // (if packaging) add a dependency for the property's data-type
                            if (direction == Action.Packaging)
                                item.Dependencies.Add(firstProperty.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                        }
                    }
                }
            }

            //Iterate property values and construct a JObject so
            //the json isn't converted into a string.
            var resolvedValues = new JObject();
            foreach (var val in propertyValues)
            {
                resolvedValues.Add(new JProperty(val.Key, val.Value));
            }
            cell.Value["value"] = resolvedValues;
        }
    }
}
