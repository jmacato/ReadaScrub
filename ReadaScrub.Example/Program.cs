using System;
using System.Threading.Tasks;

namespace ReadaScrub.Example
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            //var op = new ReadaScrub.Parser("http://www.iflscience.com/health-and-medicine/nonsurgical-procedure-returns-hand-function-to-paralyzed-people/all");
            //  var op = new ReadaScrub.Parser(" https://news.abs-cbn.com/news/05/01/18/15-hectares-of-boracay-up-for-land-reform-dar");
            //   var op = new ReadaScrub.Parser("https://arstechnica.com/science/2018/04/the-ethics-of-growing-complex-structures-with-human-brain-cells/");
           // var op = new ReadaScrub.Parser("https://phys.org/news/2018-05-metal-free-metamaterial-swiftly-tuned-electromagnetic.html");
            var op = new ReadaScrub.Parser("https://www.sciencealert.com/european-physicists-just-tested-quantum-entanglement-in-massive-clouds-of-atoms");

            


            //var op = new ReadaScrub.Parser("https://hackaday.com/2018/05/01/battery-backup-conceals-a-pentesting-pi/");
            //var op = new ReadaScrub.Parser("https://www.extremetech.com/extreme/268543-water-based-battery-could-boost-solar-and-wind-power");
            //var op = new ReadaScrub.Parser("https://www.philstar.com/headlines/2016/08/11/1612335/alvarez-rody-prefers-federal-parliamentary-system");

            //The following has no root element for paragraphs. 
            //var op = new ReadaScrub.Parser("https://www.aclu.org/blog/privacy-technology/internet-privacy/facebook-tracking-me-even-though-im-not-facebook");


            await op.DoParseAsync();
        }
    }
}
