namespace SMS.Models
{
    public interface IEntity
    {
        bool RemoveFromContext(SmsDbContext context);
        bool AddOrUpdate(SmsDbContext context);
    }
}