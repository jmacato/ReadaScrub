#define TEST

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ReadaScrub.Example
{
    class Program
    {
        static async Task Main(string[] args)
        {
#if TEST
            var url = "https://www.minvolai.com/uploads/decruft-samples/articspecies.html";
            var op = await new Engine(url).DoParseAsync();
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
