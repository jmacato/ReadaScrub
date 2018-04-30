using System;
using System.Threading.Tasks;

namespace ReadaScrub.Example
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var k = new System.Net.Http.HttpClient();
            var uri1 = new Uri("https://www.iaea.org/topics/fusion");

           var page = await k.GetStringAsync(uri1);
           var op = new ReadaScrub.Parser(page, uri1);
           var opx = op.parse();
        }
    }
}
