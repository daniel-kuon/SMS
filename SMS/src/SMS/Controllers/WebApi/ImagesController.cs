using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.Net.Http.Headers;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Produces("application/json")]
    [Route("api/Images")]
    public class ImagesController : ApiController<Image>
    {

        [HttpPost("upload", Name = "AddFile")]
        public ActionResult UploadImage()
        {
            try
            {
                var files = Request.Form.Files;
                if (files.Count>1)
                    throw new Exception("Es kann nur eine Datei zurzeit hochgeladen werden");
                var albumId=int.Parse(Request.Form["album_id"]);
                var file = files.First();
                var fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim('"');

                var basePath = PlatformServices.Default.MapPath("~/images/upload");
                var ticks = DateTime.Now.Ticks.ToString();
                var outputDir = Path.Combine(basePath,ticks);
                Directory.CreateDirectory(outputDir);
                file.SaveAs(Path.Combine(outputDir ,fileName));
                var dimensions = System.Drawing.Image.FromFile(Path.Combine(outputDir, fileName)).Size;
               var albumImage= new AlbumImage()
                {
                    Image =
                        new Image()
                        {
                            Path = $"/images/upload/{ticks}/{fileName}",
                            Height = dimensions.Height,
                            Width = dimensions.Width
                        },
                    AlbumId = albumId
                };
                albumImage.AddOrUpdate(Context);
                Context.SaveChanges();
                return Ok(albumImage.Image);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.Message) {StatusCode = 500};
            }
        }

        public ImagesController(SmsDbContext context) : base(context)
        {
        }
    }
}