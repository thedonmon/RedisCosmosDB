using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace RestaurantLocation.Functions.Models
{
    public class RestaurantLocationConfigurationDTO
    {
        public string CountryCode { get; set; }
        public int RestaurantId { get; set; }
        public List<string> AllowedZones { get; set; } = new List<string>();
        public bool IsActive { get; set; }
        [JsonProperty("id")]
        public Guid id { get; set; } = Guid.NewGuid();
        public DateTimeOffset InsertDate { get; set; }
        public DateTimeOffset UpdateDate { get; set; }

    }
}
