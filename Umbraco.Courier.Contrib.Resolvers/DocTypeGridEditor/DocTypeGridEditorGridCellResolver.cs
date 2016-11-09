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

namespace Umbraco.Courier.Contrib.Resolvers.DocTypeGridEditor
{
    public class DocTypeGridEditorGridCellResolver : GridCellResolverProvider
    {
        /// <summary>
        /// Indicates if we are packaging or extracting.
        /// </summary>
        private enum Action
        {
            Packaging,
            Extracting
        }

        public override bool ShouldRun(string view, GridValueControlModel cell)
        {
            try
            {
                if (cell == null || cell.Value == null)
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
            var docTypeAlias = cell.Value["dtgeContentTypeAlias"].ToString();
            if (string.IsNullOrWhiteSpace(docTypeAlias))
                return;

            var cellValue = cell.Value["value"].ToString();
            if (string.IsNullOrWhiteSpace(cellValue))
                return;

            var data = JsonConvert.DeserializeObject(cellValue);
            if (!(data is JObject))
                return;

            var propValues = ((JObject)data).ToObject<Dictionary<string, object>>();
            var docType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(
                new ItemIdentifier(docTypeAlias, ItemProviderIds.documentTypeItemProviderGuid));

            if (direction == Action.Packaging)
            {
                item.Dependencies.Add(docType.UniqueId.ToString(), ItemProviderIds.documentTypeItemProviderGuid);
            }

            var propertyItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

            var properties = docType.Properties;

            // check for compositions
            foreach (var masterTypeAlias in docType.MasterDocumentTypes)
            {
                var masterType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(masterTypeAlias, ItemProviderIds.documentTypeItemProviderGuid));
                if (masterType != null)
                    properties.AddRange(masterType.Properties);
            }

            foreach (var prop in properties)
            {
                object value = null;
                if (!propValues.TryGetValue(prop.Alias, out value) || value == null)
                    continue;

                var datatype =
                    ExecutionContext.DatabasePersistence.RetrieveItem<DataType>(
                        new ItemIdentifier(
                            prop.DataTypeDefinitionId.ToString(),
                            ItemProviderIds.dataTypeItemProviderGuid));

                var fakeItem = new ContentPropertyData
                {
                    ItemId = item.ItemId,
                    Name = string.Format("{0} [{1}: Nested {2} ({3})]", item.Name, EditorAlias, datatype.PropertyEditorAlias, prop.Alias),
                    Data = new List<ContentProperty>
                    {
                        new ContentProperty
                        {
                            Alias = prop.Alias,
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
                        // run the 'fake' item through Courier's data resolvers
                        ResolutionManager.Instance.PackagingItem(fakeItem, propertyItemProvider);
                    }
                    catch (Exception ex)
                    {
                        CourierLogHelper.Error<DocTypeGridEditorGridCellResolver>(
                            string.Concat("Error packaging data value: ", fakeItem.Name), ex);
                    }
                }
                else if (direction == Action.Extracting)
                {
                    try
                    {
                        // run the 'fake' item through Courier's data resolvers
                        ResolutionManager.Instance.ExtractingItem(fakeItem, propertyItemProvider);
                    }
                    catch (Exception ex)
                    {
                        CourierLogHelper.Error<DocTypeGridEditorGridCellResolver>(
                            string.Concat("Error extracting data value: ", fakeItem.Name), ex);
                    }
                }

                // pass up the dependencies and resources
                item.Dependencies.AddRange(fakeItem.Dependencies);
                item.Resources.AddRange(fakeItem.Resources);

                if (fakeItem.Data != null && fakeItem.Data.Any())
                {
                    var firstDataType = fakeItem.Data.FirstOrDefault();
                    if (firstDataType != null)
                    {
                        // set the resolved property data value
                        propValues[prop.Alias] = firstDataType.Value;

                        // (if packaging) add a dependency for the property's data-type
                        if (direction == Action.Packaging)
                            item.Dependencies.Add(firstDataType.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                    }
                }
            }

            // build up json as a string first, as directly converting 
            // propValues to a JToken causes json objects to be converted into a string
            // (such as nested content inside a doctypegrid)
            var jsonString = new StringBuilder("{");
            foreach (var val in propValues)
            {
                jsonString.Append("\"");
                jsonString.Append(val.Key);
                jsonString.Append("\":");

                // check if it's a json object and not just a string
                if (val.Value.ToString().Trim().StartsWith("["))
                {
                    jsonString.Append(val.Value);
                }
                else
                {
                    jsonString.Append("\"");
                    jsonString.Append(val.Value);
                    jsonString.Append("\"");
                }

                jsonString.Append(",");
            }
            if (jsonString.Length > 1)
            {
                jsonString.Remove(jsonString.Length - 1, 1);
            }
            jsonString.Append("}");

            var tempCellValue = JToken.Parse(jsonString.ToString());
            cell.Value["value"] = tempCellValue;
        }
    }
}
