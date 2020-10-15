using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RestaurantLocation.Functions.Models
{
    public class RestaurantLocationRequest
    {
        [Required]
        public string CountryCode { get; set; }
        [Required]
        public int RestaurantId { get; set; }
        public Guid id { get; set; }
        public List<string> AllowedZones { get; set; } = new List<string>();
        public bool IsActive { get; set; }
    }
}
