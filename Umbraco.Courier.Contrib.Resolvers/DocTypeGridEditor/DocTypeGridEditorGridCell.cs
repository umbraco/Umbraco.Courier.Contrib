using System;
using System.Collections.Generic;
using System.Linq;
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
        private enum Direction
        {
            Extracting,
            Packaging
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
            ReplacePropertyDataIds(item, propertyData, cell, Direction.Packaging);
        }

        public override void ExtractingCell(Item item, ContentProperty propertyData, GridValueControlModel cell)
        {
            ReplacePropertyDataIds(item, propertyData, cell, Direction.Extracting);
        }

        private void ReplacePropertyDataIds(Item item, ContentProperty propertyData, GridValueControlModel cell, Direction direction)
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

            if (direction == Direction.Packaging)
            {
                item.Dependencies.Add(docType.UniqueId.ToString(), ItemProviderIds.documentTypeItemProviderGuid);
            }

            var propertyItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

            foreach (var prop in docType.Properties)
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
                            Value = value.ToString()
                        }
                    }
                };

                if (direction == Direction.Packaging)
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
                else if (direction == Direction.Extracting)
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
                        if (direction == Direction.Packaging)
                            item.Dependencies.Add(firstDataType.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                    }
                }
            }

            cell.Value["value"] = JToken.FromObject(propValues);
        }
    }
}
