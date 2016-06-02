using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SMS.Models
{
    public class Comment:Entity
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public double Rating { get; set; }
        [Required]
        public int? ParentId { get; set; }
    }
}