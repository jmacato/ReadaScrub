using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ReadaScrub.Example
{
    class Program
    {

         
        static async Task Main(string[] args)
        {
#if DEBUG
            var url = "https://www.gamespot.com/articles/avengers-infinity-war-directors-discuss-the-ending/1100-6458625/";
            var op = await new Engine(url).DoParseAsync();
          //  var template = await System.IO.File.ReadAllTextAsync("template.html");
          //  template = template.Replace("$CONTENT$", op.Content);
            await System.IO.File.WriteAllTextAsync("sample.html", op.Content);
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
