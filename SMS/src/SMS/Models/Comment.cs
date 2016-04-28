using System.Collections.Generic;

namespace SMS.Models
{
    public class Comment:Entity
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public double Rating { get; set; }
        public int ParentId { get; set; }
        
    }
}