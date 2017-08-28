using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Hosting;
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
        private static Lazy<Version> UrlPickerVersion
        {
            get
            {
                return new Lazy<Version>(() =>
                {
                    // we cannot just get the assembly and get version from that, as we need to check the FileVersion
                    // (apparently that is different from the assembly version...)
                    var urlPickerAssemblyPath = HostingEnvironment.MapPath("~/bin/UrlPicker.dll");
                    if (IO.FileExists(urlPickerAssemblyPath))
                    {
                        var fileVersionInfo = FileVersionInfo.GetVersionInfo(urlPickerAssemblyPath);
                        return new Version(fileVersionInfo.FileVersion);
                    }
                    return null;
                });
            }
        }

        // from the versions found on Our - this version and forward stores arrays
        private readonly Version UrlPickerVersionStoringArray = new Version(0, 15, 0, 1);
        
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

                IEnumerable<UrlPickerPropertyData> urlPickerPropertyDatas;

                // If using an old version of the UrlPicker - data is serialized as a single object
                // Otherwise data is serialized as an array of objects, even if only one is selected.
                if (UrlPickerVersion.Value < UrlPickerVersionStoringArray)
                {
                    var urlPickerPropertyData = JsonConvert.DeserializeObject<UrlPickerPropertyData>(propertyData.Value.ToString());
                    urlPickerPropertyDatas = new List<UrlPickerPropertyData> {urlPickerPropertyData};
                }
                else
                {
                    urlPickerPropertyDatas = JsonConvert.DeserializeObject<IEnumerable<UrlPickerPropertyData>>(propertyData.Value.ToString());
                }

                var resolvedPropertyDatas = new List<UrlPickerPropertyData>();

                foreach (var urlPickerProperty in urlPickerPropertyDatas)
                {
                    //Check for the 'content' type
                    if (urlPickerProperty.Type.Equals("content", StringComparison.OrdinalIgnoreCase) &&
                        urlPickerProperty.TypeData.ContentId != null &&
                        (urlPickerProperty.TypeData.ContentId is int || urlPickerProperty.TypeData.ContentId is Int64))
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
                        resolvedPropertyDatas.Add(packagedContentData);
                        continue;
                    }

                    //Check for the 'media' type
                    if (urlPickerProperty.Type.Equals("media", StringComparison.OrdinalIgnoreCase) &&
                        urlPickerProperty.TypeData.MediaId != null &&
                        (urlPickerProperty.TypeData.MediaId is int || urlPickerProperty.TypeData.MediaId is Int64))
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
                        resolvedPropertyDatas.Add(packagedMediaData);
                        continue;
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
                    resolvedPropertyDatas.Add(packagedData);
                }

                // If using an old version of the UrlPicker - data is serialized as a single object
                // Otherwise data is serialized as an array of objects, even if only one is selected.
                if (resolvedPropertyDatas.Count == 1 && UrlPickerVersion.Value < UrlPickerVersionStoringArray)
                    propertyData.Value = JsonConvert.SerializeObject(resolvedPropertyDatas[0]);
                else
                    propertyData.Value = JsonConvert.SerializeObject(resolvedPropertyDatas);
            }
            else
            {
                //The Courier file has data as UrlPickerPropertyData w/ids as guids, so we want to look for those guids and find their integer ids
                //so the object can be saved as (the original) json in the Umbraco database.

                IEnumerable<UrlPickerPropertyData> packagedUrlPickerPropertyDatas;

                // If using an old version of the UrlPicker - data is serialized as a single object
                // Otherwise data is serialized as an array of objects, even if only one is selected.
                if (UrlPickerVersion.Value < UrlPickerVersionStoringArray)
                {
                    var urlPickerPropertyData = JsonConvert.DeserializeObject<UrlPickerPropertyData>(propertyData.Value.ToString());
                    packagedUrlPickerPropertyDatas = new List<UrlPickerPropertyData> { urlPickerPropertyData };
                }
                else
                {
                    packagedUrlPickerPropertyDatas = JsonConvert.DeserializeObject<IEnumerable<UrlPickerPropertyData>>(propertyData.Value.ToString());
                }
 
                var resolvedPropertyDatas = new List<UrlPickerPropertyData>();

                foreach (var packagedUrlPickerProperty in packagedUrlPickerPropertyDatas)
                {
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
                            resolvedPropertyDatas.Add(contentData);
                            continue;
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
                            resolvedPropertyDatas.Add(mediaData);
                            continue;
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
                    resolvedPropertyDatas.Add(extractedData);
                }

                // If using an old version of the UrlPicker - data is serialized as a single object
                // Otherwise data is serialized as an array of objects, even if only one is selected.
                if (resolvedPropertyDatas.Count == 1 && UrlPickerVersion.Value < UrlPickerVersionStoringArray)
                    propertyData.Value = JsonConvert.SerializeObject(resolvedPropertyDatas[0]);
                else
                    propertyData.Value = JsonConvert.SerializeObject(resolvedPropertyDatas);
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