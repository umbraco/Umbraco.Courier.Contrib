using System;
using Newtonsoft.Json;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.Enums;
using Umbraco.Courier.Core.Helpers;
using Umbraco.Courier.DataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Umbraco.Courier.Contrib.Resolvers.PropertyDataResolvers
{
    /// <summary>
    /// Url Picker Property Data Resolver for the Imulus Url Picker DataType
    /// </summary>
    public class ImulusUrlPickerPropertyDataResolver : PropertyDataResolverProvider
    {
        /// <summary>
        /// Alias of the editor this resolver should trigger on.
        /// </summary>
        public override string EditorAlias
        {
            get { return "Imulus.UrlPicker"; }
        }

        /// <summary>
        /// This is triggered when the property is about to be packaged (Umbraco -> Courier).
        /// </summary>
        /// <param name="item">Item being packaged</param>
        /// <param name="propertyData">UrlPicker property being packaged</param>
        public override void PackagingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Packaging);
            base.PackagingProperty(item, propertyData);
        }

        /// <summary>
        /// This is triggered when the property is about to be extracted (Courier -> Umbraco).
        /// </summary>
        /// <param name="item">Item being extracted</param>
        /// <param name="propertyData">UrlPicker property being extracted</param>
        public override void ExtractingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Action.Extracting);
            base.ExtractingProperty(item, propertyData);
        }

        private void ProcessPropertyData(Item item, ContentProperty propertyData, Action action)
        {
            if (action == Action.Packaging)
            {
                //Umbraco stores the UrlPickerPropertyData w/integer ids as json in the database, so we want to look for the those 
                //integer ids and convert them to guids. The resulting object will be saved to the Courier file.
                var urlPickerProperty = JsonConvert.DeserializeObject<UrlPickerPropertyData>(propertyData.Value.ToString());

                //Check for the 'content' type
                if (urlPickerProperty.Type.Equals("content", StringComparison.OrdinalIgnoreCase) && urlPickerProperty.TypeData.ContentId != null && urlPickerProperty.TypeData.ContentId is int)
                {
                    var identifier = Dependencies.ConvertIdentifier(urlPickerProperty.TypeData.ContentId.ToString(), IdentifierReplaceDirection.FromNodeIdToGuid, ExecutionContext);
                    //Add the referenced Content item as a dependency
                    item.Dependencies.Add(identifier, ItemProviderIds.documentItemProviderGuid);
                    var packagedContentData = new UrlPickerPropertyData
                    {
                        Type = urlPickerProperty.Type,
                        Meta = urlPickerProperty.Meta,
                        TypeData = new TypeData { Url = urlPickerProperty.TypeData.Url, ContentId = new Guid(identifier) }
                    };
                    propertyData.Value = JsonConvert.SerializeObject(packagedContentData);
                    return;
                }

                //Check for the 'media' type
                if (urlPickerProperty.Type.Equals("media", StringComparison.OrdinalIgnoreCase) &&
                    urlPickerProperty.TypeData.MediaId != null && urlPickerProperty.TypeData.MediaId is int)
                {
                    var identifier = Dependencies.ConvertIdentifier(urlPickerProperty.TypeData.MediaId.ToString(), IdentifierReplaceDirection.FromNodeIdToGuid, ExecutionContext);
                    //Add the referenced Content item as a dependency
                    item.Dependencies.Add(identifier, ItemProviderIds.mediaItemProviderGuid);
                    var packagedMediaData = new UrlPickerPropertyData
                    {
                        Type = urlPickerProperty.Type,
                        Meta = urlPickerProperty.Meta,
                        TypeData = new TypeData { Url = urlPickerProperty.TypeData.Url, MediaId = new Guid(identifier) }
                    };
                    propertyData.Value = JsonConvert.SerializeObject(packagedMediaData);
                    return;
                }

                //Convert the object to get the right types in the json output
                var packagedData = new UrlPickerPropertyData
                {
                    Type = urlPickerProperty.Type,
                    Meta = urlPickerProperty.Meta,
                    TypeData = new TypeData { Url = urlPickerProperty.TypeData.Url }
                };
                //Since the Imulus.UrlPicker is already handled in Archetype we need to ensure that guids that are already resolved
                //get passed on through to the property data value (for both Content and Media)
                if (urlPickerProperty.TypeData.ContentId != null && urlPickerProperty.TypeData.ContentId is string)
                {
                    packagedData.TypeData.ContentId = new Guid(urlPickerProperty.TypeData.ContentId.ToString());
                    item.Dependencies.Add(urlPickerProperty.TypeData.ContentId.ToString(), ItemProviderIds.documentItemProviderGuid);
                }
                if (urlPickerProperty.TypeData.MediaId != null && urlPickerProperty.TypeData.MediaId is string)
                {
                    packagedData.TypeData.ContentId = new Guid(urlPickerProperty.TypeData.MediaId.ToString());
                    item.Dependencies.Add(urlPickerProperty.TypeData.MediaId.ToString(), ItemProviderIds.documentItemProviderGuid);
                }
                propertyData.Value = JsonConvert.SerializeObject(packagedData);
            }
            else
            {
                //The Courier file has data as UrlPickerPropertyData w/ids as guids, so we want to look for those guids and find their integer ids
                //so the object can be saved as (the original) json in the Umbraco database.
                var packagedUrlPickerProperty = JsonConvert.DeserializeObject<UrlPickerPropertyData>(propertyData.Value.ToString());

                //Check for the 'content' type
                if (packagedUrlPickerProperty.Type.Equals("content", StringComparison.OrdinalIgnoreCase) &&
                    packagedUrlPickerProperty.TypeData.ContentId != null)
                {
                    var identifier = Dependencies.ConvertIdentifier(packagedUrlPickerProperty.TypeData.ContentId.ToString(), IdentifierReplaceDirection.FromGuidToNodeId, ExecutionContext);
                    int id;
                    if (int.TryParse(identifier, out id))
                    {
                        var contentData = new UrlPickerPropertyData
                        {
                            Type = packagedUrlPickerProperty.Type,
                            Meta = packagedUrlPickerProperty.Meta,
                            TypeData = new TypeData
                            {
                                Url = packagedUrlPickerProperty.TypeData.Url,
                                ContentId = id
                            }
                        };
                        propertyData.Value = JsonConvert.SerializeObject(contentData);
                        return;
                    }
                }

                //Check for the 'media' type
                if (packagedUrlPickerProperty.Type.Equals("media", StringComparison.OrdinalIgnoreCase) &&
                    packagedUrlPickerProperty.TypeData.MediaId != null)
                {
                    var identifier = Dependencies.ConvertIdentifier(packagedUrlPickerProperty.TypeData.MediaId.ToString(), IdentifierReplaceDirection.FromGuidToNodeId, ExecutionContext);
                    int id;
                    if (int.TryParse(identifier, out id))
                    {
                        var mediaData = new UrlPickerPropertyData
                        {
                            Type = packagedUrlPickerProperty.Type,
                            Meta = packagedUrlPickerProperty.Meta,
                            TypeData = new TypeData
                            {
                                Url = packagedUrlPickerProperty.TypeData.Url,
                                MediaId = id
                            }
                        };
                        propertyData.Value = JsonConvert.SerializeObject(mediaData);
                        return;
                    }
                }

                //Convert the object to get the right types in the json output
                var extractedData = new UrlPickerPropertyData
                {
                    Type = packagedUrlPickerProperty.Type,
                    Meta = packagedUrlPickerProperty.Meta,
                    TypeData = new TypeData { Url = packagedUrlPickerProperty.TypeData.Url }
                };
                //As a precaution we check if the content and media ids already exist in which case we keep them
                //as they might already have been handled by Archetype
                if (packagedUrlPickerProperty.TypeData.ContentId != null)
                {
                    extractedData.TypeData.ContentId = packagedUrlPickerProperty.TypeData.ContentId;
                }
                if (packagedUrlPickerProperty.TypeData.MediaId != null)
                {
                    extractedData.TypeData.MediaId = packagedUrlPickerProperty.TypeData.MediaId;
                }
                propertyData.Value = JsonConvert.SerializeObject(extractedData);
            }
        }

        internal class UrlPickerPropertyData
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("meta")]
            public Meta Meta { get; set; }
            [JsonProperty("typeData")]
            public TypeData TypeData { get; set; }
        }

        internal class Meta
        {
            [JsonProperty("title")]
            public string Title { get; set; }
            [JsonProperty("newWindow")]
            public bool NewWindow { get; set; }
        }

        internal class TypeData
        {
            [JsonProperty("url")]
            public string Url { get; set; }
            [JsonProperty("contentId")]
            public object ContentId { get; set; }
            [JsonProperty("mediaId")]
            public object MediaId { get; set; }
        }
    }
}