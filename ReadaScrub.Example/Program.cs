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
            "http://www.iflscience.com/editors-blog/10-popular-life-hacks-that-are-completely-bogus/"
        };

        static async Task Main(string[] args)
        {
#if DEBUG
            foreach (var url in testURLS)
            {
                var uri = new Uri(url);
                var op = await new Engine(url).DoParseAsync();
                System.IO.Directory.CreateDirectory($"{uri.Host}/");
                await System.IO.File.WriteAllTextAsync(uri.Host + "/output.html", op.Content);
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
