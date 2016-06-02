using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMS.Models
{
    [Table("ContentPage")]
    public class ContentPage:Entity
    {
        [Required]
        public string Title { get; set; }
        [Required]
        public string Content { get; set; }
        
    }
}