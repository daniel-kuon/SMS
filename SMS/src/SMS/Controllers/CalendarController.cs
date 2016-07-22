using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers
{
    [Route("api/calendar")]
    public class CalendarController : Controller
    {
        public CalendarController(SmsDbContext dbContext)
        {
            DbContext = dbContext;
        }

        public SmsDbContext DbContext { get; set; }


        [HttpGet("seed")]
        public HttpResponse Dummies()
        {
            var calendar=new Calendar();
            foreach (var trip in DbContext.Set<Trip>().Where(t=>t.IsDummy))
            {
                calendar.Events.Add(new Event()
                {
                    End = trip.EndDate,
                    Start = trip.StartDate,
                    Title = trip.Crew.Select(c=>c.Person.FirstName).Aggregate((s1,s2)=>s1+", " + s2)
                });
            }
            
            Response.ContentType = "text/ical";
                    var sw=new StreamWriter(Response.Body);
            sw.Write(calendar.ToString());
            return Response;
        }
    }

    public class Calendar
    {
        public List<Event> Events { get; set; } =new List<Event>();
        public override string ToString()
        {var b=new StringBuilder();
            b.AppendLine("BEGIN:VCALENDAR");
            b.AppendLine("VERSION:2.0");
            b.AppendLine("METHOD:PUBLISH");
            Events.ForEach(e=>b.Append(e));
            b.AppendLine("END:VCALENDAR");
            return b.ToString();
        }
    }

    public class Event
    {
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public string HtmlDescriptionUrl { get; set; }
        public string Uid { get; set; }

        public override string ToString()
        {
            var b = new StringBuilder();
            b.AppendLine("BEGIN:VEVENT");
            if (End != null)
                b.AppendLine("DTEND:" + End.Value.ToString("yyyyMMddTHHmmssZ"));
            if (Start != null)
                b.AppendLine("DTSTART:" + Start.Value.ToString("yyyyMMddTHHmmssZ"));
            if (!string.IsNullOrEmpty(Title))
                b.AppendLine("SUMMARY:" + Title);
            if (!string.IsNullOrEmpty(Location))
                b.AppendLine("LOCATION:" + Location);
            if (!string.IsNullOrEmpty(Description))
            {
                if (!string.IsNullOrEmpty(HtmlDescriptionUrl))
                    b.AppendLine($"DESCRIPTION;ALTREP=\"http://{HtmlDescriptionUrl}\":{Description}");
                else
                    b.AppendLine("DESCRIPTION:" + Description);
            }
            else if (!string.IsNullOrEmpty(HtmlDescriptionUrl))
                b.AppendLine($"DESCRIPTION;ALTREP=\"http://{HtmlDescriptionUrl}\":");
            b.AppendLine("DTSTAMP:" + DateTime.Now.ToString("yyyyMMddTHHmmssZ"));
            b.AppendLine("END:VEVENT");
            return b.ToString();
        }
    }
}