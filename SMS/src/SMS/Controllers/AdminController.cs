using Microsoft.AspNet.Mvc;

namespace SMS.Controllers
{
    public class AdminController:Controller
    {

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Map()
        {
            return View();
        }

        public IActionResult People()
        {
            return View();
        }

    }
}