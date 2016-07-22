using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/ContentPages")]
    public class ContentPagesController : ApiController<ContentPage>
    {
        public ContentPagesController(SmsDbContext context) : base(context)
        {
        }
    }
}