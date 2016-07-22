using System;
using System.IO;
using System.Linq;
using ImageProcessorCore;
using ImageProcessorCore.Samplers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
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
        private IHostingEnvironment _env;


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

                var basePath=Path.Combine(_env.WebRootPath,"images\\upload");
                var ticks = DateTime.Now.Ticks.ToString();
                var outputDir = Path.Combine(basePath, ticks);
                Directory.CreateDirectory(outputDir);
                using (var fS = new FileStream(Path.Combine(outputDir, fileName), FileMode.Create, FileAccess.Write))
                {
                    file.CopyTo(fS);
                    fS.Flush();
                }


                AlbumImage albumImage;
                using (var originalStream = System.IO.File.OpenRead(Path.Combine(outputDir, fileName)))
                {
                    var thumb = new ImageProcessorCore.Image(originalStream);
                    albumImage = new AlbumImage()
                    {
                        Image =
                            new Image()
                            {
                                Path = $"/images/upload/{ticks}/{fileName}",
                                Height = thumb.Height,
                                Width = thumb.Width
                            },
                        AlbumId = albumId
                    };
                    albumImage.AddOrUpdate(Context);

                    var thumbHeight = 180;
                    var thumbWidth = Convert.ToInt32(Math.Round((decimal)thumb.Width / ((decimal)thumb.Height / 180)));
                    if (thumbWidth > 190)
                    {
                        thumbWidth = 190;
                        thumbHeight = Convert.ToInt32(Math.Round((decimal)thumb.Height / ((decimal)thumb.Width / 190)));
                    }
                    var thumbOutputDir = Path.Combine(_env.WebRootPath, "thumbs\\images\\upload\\" + ticks);
                    Directory.CreateDirectory(thumbOutputDir);
                    using (var thumbStream = System.IO.File.Create(Path.Combine(thumbOutputDir, fileName + ".jpg")))
                    {
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

        public ImagesController(SmsDbContext context, IHostingEnvironment hostingEnvironment) : base(context)
        {
            _env = hostingEnvironment;
        }
    }
}