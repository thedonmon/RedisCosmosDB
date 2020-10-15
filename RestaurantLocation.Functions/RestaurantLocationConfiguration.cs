using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestaurantLocation.Functions.Models;
using Microsoft.Azure.Documents.Client;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using StackExchange.Redis;
using Microsoft.Extensions.Caching.Distributed;
using RestaurantLocation.Functions.Helpers;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace RestaurantConfiguration.Functions
{
    public class RestaurantLocationConfiguration
    {
        private readonly IDistributedCache _cache;
        private readonly IConfiguration _config;
        private const string RedisKey = "RestaurantLocationKey";
        public RestaurantLocationConfiguration(IDistributedCache cache, IConfiguration config)
        {
            _cache = cache;
            _config = config;
        }

        [FunctionName("UpsertRestaurantLocationConfiguration")]
        public async Task<IActionResult> UpsertRestaurantLocationConfiguration(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Upsert")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "CosmosConnection")] DocumentClient client,
            ILogger log)
        {
            string requestBody = string.Empty;
            using(var stream = new StreamReader(req.Body))
            {
                requestBody = await stream.ReadToEndAsync();
            }
            RestaurantLocationRequest data = JsonConvert.DeserializeObject<RestaurantLocationRequest>(requestBody);
            log.LogInformation("Executing upsert with request data: {FCData}", requestBody);

            try
            {
                Uri collectionUri = UriFactory.CreateDocumentCollectionUri(databaseId: _config["DatabaseId"], collectionId: _config["CollectionId"]);

                var documentToUpdate = client.CreateDocumentQuery<RestaurantLocationConfigurationDTO>(collectionUri, null).Where(x => x.FulfillmentCenterId == data.FulfillmentCenterId).ToList();
                if (documentToUpdate.Any())
                {
                    foreach (var item in documentToUpdate)
                    {
                        item.AllowedCountries = data.AllowedCountries;
                        item.FulfillmentCenterId = data.FulfillmentCenterId;
                        item.UpdateDate = DateTimeOffset.UtcNow;
                        item.IsActive = data.IsActive;
                        await client.UpsertDocumentAsync(collectionUri, item);
                    }
                }
                else
                {
                    var itemToAdd = new RestaurantLocationConfigurationDTO()
                    {
                        AllowedCountries = data.AllowedCountries,
                        CountryCode = data.CountryCode,
                        FulfillmentCenterId = data.FulfillmentCenterId,
                        InsertDate = DateTimeOffset.UtcNow,
                        IsActive = data.IsActive
                        
                    };
                    await client.UpsertDocumentAsync(collectionUri, itemToAdd);
                }
            }
            catch(Exception ex)
            {
                log.LogError(ex, $"Failed to upsert {data.FulfillmentCenterId}");
                return new BadRequestObjectResult($"Failed to upsert {data.FulfillmentCenterId}");
            }

            return new OkObjectResult($"Restaurant {data.FulfillmentCenterId} upserted");
        }

        [FunctionName("UpdateRestaurantLocationConfigurationCache")]
        public async Task UpdateRestaurantLocationConfigurationCache(
            [CosmosDBTrigger(
                                databaseName: "%DatabaseId%",
                                collectionName: "%CollectionId%",
                                LeaseCollectionPrefix = "FulfillmentCenterUpdated",
                                ConnectionStringSetting = "CosmosConnection", CreateLeaseCollectionIfNotExists = true, StartFromBeginning =true)] 
            IReadOnlyList<Document> fulfillmentCenters,
            [CosmosDB(ConnectionStringSetting = "CosmosConnection")] DocumentClient client,
            ILogger log)
        {
            var cacheItems = await _cache.GetAsync<List<RestaurantLocationConfigurationDTO>>(RedisKey);
            log.LogInformation("Items loaded from cache: {count}", cacheItems?.Count() ?? 0);
            if (cacheItems == null || !cacheItems.Any())
            {
                Uri collectionUri = UriFactory.CreateDocumentCollectionUri(databaseId: _config["DatabaseId"], collectionId: _config["CollectionId"]);
                List<string> documentsJson = new List<string>();
                string continuationToken = null;
                do
                {
                    var feed = await client.ReadDocumentFeedAsync(
                        collectionUri,
                        new FeedOptions { MaxItemCount = 10, RequestContinuation = continuationToken });
                    continuationToken = feed.ResponseContinuation;
                    foreach (Document document in feed)
                    {
                        documentsJson.Add(document.ToString());
                    }
                } while (continuationToken != null);
                var objectsToCache = documentsJson.Select(x => JsonConvert.DeserializeObject<RestaurantLocationConfigurationDTO>(x));
                await _cache.SetAsync(RedisKey, objectsToCache, TimeSpan.FromDays(365)).ConfigureAwait(false);
            }
            else
            {
                var replaceExistingFCJson = fulfillmentCenters.Where(x => cacheItems.Select(y => y.id).Contains(Guid.Parse(x.Id))).Select(d => d.ToString());
                if (replaceExistingFCJson.Any())
                {
                    var FCsToUpdate = replaceExistingFCJson.Select(x => JsonConvert.DeserializeObject<RestaurantLocationConfigurationDTO>(x)).ToList();
                    log.LogInformation("Updating {count} items in Redis", FCsToUpdate.Count());
                    cacheItems.RemoveAll(x => FCsToUpdate.Select(y => y.id).Contains(x.id));
                    cacheItems.AddRange(FCsToUpdate);
                }
                else
                {
                    var newFCToAdd = fulfillmentCenters.Where(x => !cacheItems.Select(y => y.id).Contains(Guid.Parse(x.Id))).Select(d => d.ToString());
                    if (newFCToAdd.Any())
                    {
                        var FCsToAdd = newFCToAdd.Select(x => JsonConvert.DeserializeObject<RestaurantLocationConfigurationDTO>(x)).ToList();
                        log.LogInformation("Adding {count} items in Redis", newFCToAdd.Count());
                        cacheItems.AddRange(FCsToAdd);
                    }
                }
                await _cache.SetAsync(RedisKey, cacheItems, TimeSpan.FromDays(365)).ConfigureAwait(false);
            }
        }

        [FunctionName("LoadRestaurantLocationConfigurationCache")]
        public async Task<List<RestaurantLocationConfigurationDTO>> LoadFulfillmentCountryFromCache(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "LoadRestaurantLocationCache")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "CosmosConnection")] DocumentClient client,
            ILogger log)
        {
            var cacheItems = await _cache.GetAsync<List<RestaurantLocationConfigurationDTO>>(RedisKey);
            log.LogInformation("Items loaded from cache: {count}", cacheItems?.Count() ?? 0);
            if (cacheItems == null || !cacheItems.Any())
            {
                Uri collectionUri = UriFactory.CreateDocumentCollectionUri(databaseId: _config["DatabaseId"], collectionId: _config["CollectionId"]);
                List<string> documentsJson = new List<string>();
                string continuationToken = null;
                do
                {
                    var feed = await client.ReadDocumentFeedAsync(
                        collectionUri,
                        new FeedOptions { MaxItemCount = 10, RequestContinuation = continuationToken });
                    continuationToken = feed.ResponseContinuation;
                    foreach (Document document in feed)
                    {
                        documentsJson.Add(document.ToString());
                    }
                } while (continuationToken != null);
                var objectsToCache = documentsJson.Select(x => JsonConvert.DeserializeObject<RestaurantLocationConfigurationDTO>(x));
                await _cache.SetAsync(RedisKey, objectsToCache, TimeSpan.FromDays(365)).ConfigureAwait(false);
            }
            return cacheItems.Where(x => x.IsActive).ToList();
        }
        

    }
}
