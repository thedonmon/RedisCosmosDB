using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RestaurantLocation.Functions.Helpers
{
    public static class CachingExtension
    {
        public static async Task<T> GetAsync<T>(this IDistributedCache distributedCache, string key)
        {
            var objectToReturn = await distributedCache.GetAsync(key).ConfigureAwait(false) ?? new byte[0];
            if (objectToReturn.Any())
            {
                return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(objectToReturn), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }
            return default;
        }
        public static async Task SetAsync<T>(this IDistributedCache distributedCache, string key, T @object, TimeSpan timeToLive)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = timeToLive
            };
            var cacheItem = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(@object));

            await distributedCache.SetAsync(key, cacheItem, options).ConfigureAwait(false);

        }
    }
}
