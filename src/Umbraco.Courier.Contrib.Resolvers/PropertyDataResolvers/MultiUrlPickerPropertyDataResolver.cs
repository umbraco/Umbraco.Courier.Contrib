using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.Logging;
using Umbraco.Courier.DataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Umbraco.Courier.Contrib.Resolvers.PropertyDataResolvers
{
    public class MultiUrlPickerPropertyDataResolver : PropertyDataResolverProvider
    {
        public override string EditorAlias
        {
            get
            {
                return "RJP.MultiUrlPicker";
            }
        }

        public override void PackagingProperty(Item item, ContentProperty propertyData)
        {
            if (propertyData == null || propertyData.Value == null)
                return;

            var links = JsonConvert.DeserializeObject<JArray>(propertyData.Value.ToString());
            if (links == null)
                return;

            foreach (var link in links)
            {
                var isMedia = link["isMedia"] != null || link["udi"] != null && link["udi"].ToString().StartsWith("umb://media");

                if (link["id"] != null || link["udi"] != null)
                {
                    var objectTypeId = isMedia
                        ? UmbracoNodeObjectTypeIds.Media
                        : UmbracoNodeObjectTypeIds.Document;

                    var itemProviderId = isMedia
                        ? ItemProviderIds.mediaItemProviderGuid
                        : ItemProviderIds.documentItemProviderGuid;

                    var nodeGuid = Guid.Empty;
                    if (link["udi"] != null)
                    {
                        var guidString = isMedia
                            ? link["udi"].ToString().TrimStart("umb://media/")
                            : link["udi"].ToString().TrimStart("umb://document/");

                        Guid.TryParse(guidString, out nodeGuid);
                    }
                    else if (link["id"] != null)
                    {
                        int linkIdTemp;
                        if (int.TryParse(link["id"].ToString(), out linkIdTemp))
                        {
                            nodeGuid = ExecutionContext.DatabasePersistence.GetUniqueId(linkIdTemp, objectTypeId);
                        }
                    }

                    if (Guid.Empty.Equals(nodeGuid))
                        continue;

                    var guid = nodeGuid.ToString();

                    item.Dependencies.Add(guid, itemProviderId);

                    // only need to adjust this if it was an id converted to a guid. udis are already unique and match on both source and destination
                    if (link["id"] != null)
                    {
                        link["id"] = guid;
                    }
                }
                else if (isMedia && link["url"] != null)
                {
                    try
                    {
                        var mediaId = ExecutionContext.DatabasePersistence.GetUniqueIdFromMediaFile(link["url"].ToString());

                        if (!Guid.Empty.Equals(mediaId))
                            item.Dependencies.Add(mediaId.ToString(), ItemProviderIds.mediaItemProviderGuid);
                    }
                    catch (Exception ex)
                    {
                        CourierLogHelper.Error<MultiUrlPickerPropertyDataResolver>(string.Format("Error setting media-item dependency, name={0}, url={1}", link["name"], link["url"]), ex);
                    }
                }
            }

            propertyData.Value = links.ToString();
        }

        public override void ExtractingProperty(Item item, ContentProperty propertyData)
        {
            if (propertyData.Value != null)
            {
                var links = JsonConvert.DeserializeObject<JArray>(propertyData.Value.ToString());
                if (links != null)
                {
                    foreach (var link in links)
                    {
                        var isMedia = link["isMedia"] != null || link["udi"] != null && link["udi"].ToString().StartsWith("umb://media");

                        if (link["id"] == null)
                            continue;

                        var nodeObjectType = isMedia
                            ? UmbracoNodeObjectTypeIds.Media
                            : UmbracoNodeObjectTypeIds.Document;

                        Guid nodeGuid;
                        if (Guid.TryParse(link["id"].ToString(), out nodeGuid))
                            link["id"] = ExecutionContext.DatabasePersistence.GetNodeId(nodeGuid, nodeObjectType);
                    }

                    propertyData.Value = links;
                }
            }
        }
    }
}