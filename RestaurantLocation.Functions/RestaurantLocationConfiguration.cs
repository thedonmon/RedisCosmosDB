using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestaurantLocation.Functions.Helpers;
using RestaurantLocation.Functions.Models;

namespace RestaurantLocation.Functions
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

                var documentToUpdate = client.CreateDocumentQuery<RestaurantLocationConfigurationDTO>(collectionUri, null).Where(x => x.RestaurantId == data.RestaurantId).ToList();
                if (documentToUpdate.Any())
                {
                    foreach (var item in documentToUpdate)
                    {
                        item.AllowedZones = data.AllowedZones;
                        item.RestaurantId = data.RestaurantId;
                        item.UpdateDate = DateTimeOffset.UtcNow;
                        item.IsActive = data.IsActive;
                        await client.UpsertDocumentAsync(collectionUri, item);
                    }
                }
                else
                {
                    var itemToAdd = new RestaurantLocationConfigurationDTO()
                    {
                        AllowedZones = data.AllowedZones,
                        CountryCode = data.CountryCode,
                        RestaurantId = data.RestaurantId,
                        InsertDate = DateTimeOffset.UtcNow,
                        IsActive = data.IsActive
                        
                    };
                    await client.UpsertDocumentAsync(collectionUri, itemToAdd);
                }
            }
            catch(Exception ex)
            {
                log.LogError(ex, $"Failed to upsert {data.RestaurantId}");
                return new BadRequestObjectResult($"Failed to upsert {data.RestaurantId}");
            }

            return new OkObjectResult($"Restaurant {data.RestaurantId} upserted");
        }

        [FunctionName("UpdateRestaurantLocationConfigurationCache")]
        public async Task UpdateRestaurantLocationConfigurationCache(
            [CosmosDBTrigger(
                                databaseName: "%DatabaseId%",
                                collectionName: "%CollectionId%",
                                LeaseCollectionPrefix = "RestaurantLocationUpdated",
                                ConnectionStringSetting = "CosmosConnection", CreateLeaseCollectionIfNotExists = true, StartFromBeginning =true)] 
            IReadOnlyList<Document> restaurants,
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
                var replaceExistingJson = restaurants.Where(x => cacheItems.Select(y => y.id).Contains(Guid.Parse(x.Id))).Select(d => d.ToString());
                if (replaceExistingJson.Any())
                {
                    var restaurantsToUpdate = replaceExistingJson.Select(x => JsonConvert.DeserializeObject<RestaurantLocationConfigurationDTO>(x)).ToList();
                    log.LogInformation("Updating {count} items in Redis", restaurantsToUpdate.Count());
                    cacheItems.RemoveAll(x => restaurantsToUpdate.Select(y => y.id).Contains(x.id));
                    cacheItems.AddRange(restaurantsToUpdate);
                }
                else
                {
                    var newRestaurantToAdd = restaurants.Where(x => !cacheItems.Select(y => y.id).Contains(Guid.Parse(x.Id))).Select(d => d.ToString());
                    if (newRestaurantToAdd.Any())
                    {
                        var FCsToAdd = newRestaurantToAdd.Select(x => JsonConvert.DeserializeObject<RestaurantLocationConfigurationDTO>(x)).ToList();
                        log.LogInformation("Adding {count} items in Redis", newRestaurantToAdd.Count());
                        cacheItems.AddRange(FCsToAdd);
                    }
                }
                await _cache.SetAsync(RedisKey, cacheItems, TimeSpan.FromDays(365)).ConfigureAwait(false);
            }
        }

        [FunctionName("LoadRestaurantLocationConfigurationCache")]
        public async Task<List<RestaurantLocationConfigurationDTO>> LoadRestaurantLocationFromCache(
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
