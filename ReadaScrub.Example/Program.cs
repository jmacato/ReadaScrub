using System;
using System.Threading.Tasks;

namespace ReadaScrub.Example
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");
             //var op = new ReadaScrub.Parser("https://www.iaea.org/topics/fusion");
            ///var op = new ReadaScrub.Parser("http://www.iflscience.com/health-and-medicine/nonsurgical-procedure-returns-hand-function-to-paralyzed-people/");
            var op = new ReadaScrub.Parser(" https://news.abs-cbn.com/news/05/01/18/15-hectares-of-boracay-up-for-land-reform-dar");




            await op.DoParseAsync();
        }
    }
}
