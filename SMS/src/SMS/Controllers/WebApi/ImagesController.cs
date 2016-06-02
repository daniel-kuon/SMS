using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageProcessorCore.Samplers;
using ImageProcessorCore;
using Microsoft.AspNet.Authorization;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.Net.Http.Headers;
using SMS.Models;
using Image = SMS.Models.Image;

namespace SMS.Controllers.WebApi
{
    [Produces("application/json")]
    [Route("api/Images")]
    public class ImagesController : ApiController<Image>
    {

        [Authorize]
        [HttpPost("upload", Name = "AddFile")]
        public ActionResult UploadImage()
        {
            try
            {
                var files = Request.Form.Files;
                if (files.Count > 1)
                    throw new Exception("Es kann nur eine Datei zurzeit hochgeladen werden");
                var albumId = int.Parse(Request.Form["album_id"]);
                var file = files.First();
                var fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim('"');

                var basePath = PlatformServices.Default.MapPath("~/images/upload");
                var ticks = DateTime.Now.Ticks.ToString();
                var outputDir = Path.Combine(basePath, ticks);
                Directory.CreateDirectory(outputDir);
                file.SaveAs(Path.Combine(outputDir, fileName));
                var image = System.Drawing.Image.FromFile(Path.Combine(outputDir, fileName));
                var dimensions = image.Size;
                var albumImage = new AlbumImage()
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

                var thumbHeight = 180;
                var thumbWidth = Convert.ToInt32(Math.Round((decimal)dimensions.Width / ((decimal)dimensions.Height / 180)));
                if (thumbWidth > 190)
                {
                    thumbWidth = 190;
                    thumbHeight = Convert.ToInt32(Math.Round((decimal)dimensions.Height / ((decimal)dimensions.Width / 190)));
                }

                using (var originalStream = System.IO.File.OpenRead(Path.Combine(outputDir, fileName)))
                {
                    var thumbOutputDir = PlatformServices.Default.MapPath("~/thumbs/images/upload" + "/" + ticks);
                    Directory.CreateDirectory(thumbOutputDir);
                    using (var thumbStream = System.IO.File.Create(Path.Combine(thumbOutputDir, fileName + ".jpg")))
                    {
                        var thumb = new ImageProcessorCore.Image(originalStream);
                        thumb.Resize(thumbWidth, thumbHeight).SaveAsJpeg(thumbStream, 100);
                    }
                }
                Context.SaveChanges();
                return Ok(albumImage);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.Message) { StatusCode = 500 };
            }
        }

        public ImagesController(SmsDbContext context) : base(context)
        {
        }
    }
}