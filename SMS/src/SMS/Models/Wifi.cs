using System.ComponentModel.DataAnnotations;

namespace SMS.Models
{
    public class Wifi:Entity
    {
        [Required]
        public string Name { get; set; }
        public string Password { get; set; }
        public int Speed { get; set; }
        public bool Free { get; set; }
        public int HarbourId { get; set; }
    }
}