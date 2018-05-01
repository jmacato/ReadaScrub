using System;
using System.Threading.Tasks;

namespace ReadaScrub.Example
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var op = new ReadaScrub.Parser("https://www.iaea.org/topics/fusion");
            //    var opx = op.parse();
        }
    }
}
