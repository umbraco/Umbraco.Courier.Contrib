using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.ProviderModel;
using Umbraco.Courier.DataResolvers.PropertyDataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Umbraco.Courier.Contrib.Resolvers.GridCellDataResolvers
{
    public class LeBlenderGridCellResolver : GridCellResolverProvider
    {
        public override bool ShouldRun(string view, GridValueControlModel cell)
        {
            return view.Contains("leblender");
        }

        public override void PackagingCell(Item item, ContentProperty propertyData, GridValueControlModel cell)
        {
            ProcessCell(item, propertyData, cell, Action.Packaging);
            base.PackagingCell(item, propertyData, cell);
        }

        public override void ExtractingCell(Item item, ContentProperty propertyData, GridValueControlModel cell)
        {
            ProcessCell(item, propertyData, cell, Action.Extracting);
            base.ExtractingCell(item, propertyData, cell);
        }

        private void ProcessCell(Item item, ContentProperty propertyData, GridValueControlModel cell, Action action)
        {
            // cancel if there's no values
            if (cell.Value == null || cell.Value.HasValues == false)
                return;

            var dataTypeService = ApplicationContext.Current.Services.DataTypeService;
            // get the ItemProvider for the ResolutionManager
            var propertyDataItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

            // Often there is only one entry in cell.Value, but with LeBlender 
            // min/max you can easily get 2+ entries so we'll walk them all
            var newItemValue = new JArray();
            foreach (var properties in cell.Value)
            {
                // create object to store resolved properties
                var resolvedProperties = new JObject();

                // loop through each of the property objects
                foreach (dynamic leBlenderPropertyWrapper in properties)
                {
                    // deserialize the value of the wrapper object into a LeBlenderProperty object
                    var leBlenderPropertyJson = leBlenderPropertyWrapper.Value.ToString() as string;
                    // continue if there's no data stored
                    if (string.IsNullOrEmpty(leBlenderPropertyJson)) continue;

                    var leBlenderProperty = JsonConvert.DeserializeObject<LeBlenderProperty>(leBlenderPropertyJson);

                    // get the DataType of the property
                    var dataType = dataTypeService.GetDataTypeDefinitionById(leBlenderProperty.DataTypeGuid);
                    if (dataType == null)
                    {
                        // If the data type referenced by this LeBlender Property is missing on this machine
                        // we'll get a very cryptic error when it attempts to create the pseudo property data item below.
                        // Throw a meaningful error message instead
                        throw new ArgumentNullException(string.Format("Unable to find the data type for editor '{0}' ({1} {2}) referenced by '{3}'.", leBlenderProperty.EditorName, leBlenderProperty.EditorAlias,
                            leBlenderProperty.DataTypeGuid, item.Name));
                        // Should we log a warning and continue instead?
                    }

                    // create a pseudo item for sending through resolvers
                    var pseudoPropertyDataItem = new ContentPropertyData
                    {
                        ItemId = item.ItemId,
                        Name = string.Format("{0}: (LeBlender PropertyAlias: {1}, DataTypeEditorAlias: {2})", item.Name, leBlenderProperty.EditorAlias, dataType.PropertyEditorAlias),
                        Data = new List<ContentProperty>
                        {
                            new ContentProperty
                            {
                                Alias = propertyData.Alias,
                                DataType = leBlenderProperty.DataTypeGuid,
                                PropertyEditorAlias = dataType.PropertyEditorAlias,
                                Value = leBlenderProperty.Value
                            }
                        }
                    };
                    if (action == Action.Packaging)
                    {
                        // run the resolvers (convert Ids/integers into UniqueIds/guids)
                        ResolutionManager.Instance.PackagingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                        // add in this editor's dependencies when packaging
                        item.Dependencies.AddRange(pseudoPropertyDataItem.Dependencies);
                        item.Resources.AddRange(pseudoPropertyDataItem.Resources);
                        // and include this editor's data type as a dependency too
                        item.Dependencies.Add(leBlenderProperty.DataTypeGuid.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                    }
                    else
                    {
                        // run the resolvers (convert UniqueIds/guids back to Ids/integers)
                        ResolutionManager.Instance.ExtractingItem(pseudoPropertyDataItem, propertyDataItemProvider);
                    }
                    // replace the property value with the resolved value
                    leBlenderProperty.Value = pseudoPropertyDataItem.Data.First().Value;
                    // add the resolved property to the resolved properties object
                    resolvedProperties.Add(leBlenderProperty.EditorAlias, JObject.FromObject(leBlenderProperty));
                }
                newItemValue.Add(resolvedProperties);
            }
            // replace the cell value with all the resolved values
            cell.Value = JToken.FromObject(newItemValue);
        }

        public class LeBlenderProperty
        {
            private bool _isQuoted;
            private JRaw _jrawValue;

            [JsonProperty("value")]
            public JRaw JRawValue
            {
                get { return _jrawValue; }
                set
                {
                    _jrawValue = value;
                    _isQuoted = _isQuoted || value.ToString(CultureInfo.InvariantCulture).Trim().StartsWith("\"");
                }
            }

            [JsonIgnore]
            public object Value
            {
                get
                {
                    if (_isQuoted)
                    {
                        // Umbraco.DropDown and other prevalue data types want an unquoted string
                        // so we need to strip the leading and trailing quotation marks now
                        // then put them back later
                        var trimmed = JRawValue.ToString(CultureInfo.InvariantCulture).Trim();
                        trimmed = trimmed.Substring(1, trimmed.Length - 2);
                        return trimmed;
                    }
                    // Complex data types like RJP.MultiUrlPicker expect the json as a string
                    return JRawValue.ToString(CultureInfo.InvariantCulture);
                }
                set
                {
                    string str = value as string;
                    if (str != null && _isQuoted)
                    {
                        // We stripped quotes off so put them back now
                        JRawValue = new JRaw("\"" + str + "\"");
                    }
                    else
                    {
                        JRawValue = new JRaw(value);
                    }
                }
            }

            [JsonProperty("dataTypeGuid")]
            public Guid DataTypeGuid { get; set; }

            [JsonProperty("editorAlias")]
            public string EditorAlias { get; set; }

            [JsonProperty("editorName")]
            public string EditorName { get; set; }
        }
    }
}
