namespace SMS.Models
{
    public class LogBookEntry:TackBase
    {
        public decimal MotorHoursStart { get; set; }
        public decimal MotorHoursEnd { get; set; }

        public decimal LogStart { get; set; }
        public decimal LogEnd { get; set; }

        public decimal WindSpeed { get; set; }
        public string WindDirection { get; set; }
        

        public string SpecialOccurences { get; set; }
        
    }
}