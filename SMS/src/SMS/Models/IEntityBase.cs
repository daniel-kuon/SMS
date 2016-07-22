namespace SMS.Models
{
    public interface IEntityBase
    {
        bool RemoveFromContext(SmsDbContext context);
        bool AddOrUpdate(SmsDbContext context);
    }
}