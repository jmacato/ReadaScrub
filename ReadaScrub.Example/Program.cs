using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ReadaScrub.Example
{
    class Program
    {

        static string[] testURLS = new string[]
        {
            "https://japantoday.com/category/politics/japan-gov't-website-posted-anti-korean-resident-messages",
            "https://www.philstar.com/headlines/2016/08/11/1612335/alvarez-rody-prefers-federal-parliamentary-system",
            "https://www.aclu.org/blog/privacy-technology/internet-privacy/facebook-tracking-me-even-though-im-not-facebook",
            "https://hackaday.com/2018/05/03/playing-jedi-mind-tricks-on-your-tv/",
            "http://www.iflscience.com/editors-blog/10-popular-life-hacks-that-are-completely-bogus/",
            "https://edition.cnn.com/style/article/concrete-alternatives-future-building/index.html",
            "https://phys.org/news/2018-05-multiversestephen-hawking-theory-big.html",
            "https://www.washingtonpost.com/world/asia_pacific/south-korea-dismantles-propaganda-loudspeakers-at-border/2018/05/01/21ebaf72-4d08-11e8-85c1-9326c4511033_story.html"
        };

        static async Task Main(string[] args)
        {
#if DEBUG
            foreach (var url in testURLS)
            {
                var uri = new Uri(url);
                var op = await new Engine(url).DoParseAsync();
                System.IO.Directory.CreateDirectory($"output/{uri.Host}/");
                await System.IO.File.WriteAllTextAsync($"output/{uri.Host}/output.html", op.Content);
            }
#else
            Console.Write("Enter full article URI to parse >> ");
            if (Uri.TryCreate(Console.ReadLine().Trim(), UriKind.Absolute, out var res))
            {
                var res2 = await new Parser(res.AbsoluteUri).DoParseAsync();
                if (res2.Success)
                {
                    Console.WriteLine(res2.Content);
                }
                else
                {
                    Console.WriteLine("Failed to parse the given article!");
                }
            }
            else
            {
                Console.WriteLine("Invalid URI!");
            }
#endif

        }
    }
}
