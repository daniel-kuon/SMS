namespace SMS.Models
{
    public class Address:Entity
    {
        public string Street { get; set; }
        public string Zip { get; set; }
        public string Town { get; set; }
        public string Comment { get; set; }        
        
    }
}