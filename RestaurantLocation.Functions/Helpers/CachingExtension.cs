using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RestaurantLocation.Functions.Helpers
{
    public static class CachingExtension
    {
        public async static Task<T> GetAsync<T>(this IDistributedCache distibutedCache, string key)
        {
            var objectToReturn = await distibutedCache.GetAsync(key).ConfigureAwait(false) ?? new byte[0];
            if (objectToReturn.Any())
            {
                return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(objectToReturn), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }
            return default;
        }
        public async static Task SetAsync<T>(this IDistributedCache distibutedCache, string key, T @object, TimeSpan timeToLive)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = timeToLive
            };
            var cacheItem = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(@object));

            await distibutedCache.SetAsync(key, cacheItem, options).ConfigureAwait(false);

        }
    }
}
