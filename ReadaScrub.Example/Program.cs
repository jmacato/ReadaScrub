/*

    ReadaScrub - Library for scrubbing relevant HTML content clean =) 
    Copyright (C) 2018  Jumar A. Macato.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

 */

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
            "https://phys.org/news/2018-05-multiversestephen-hawking-theory-big.html",
            "https://www.washingtonpost.com/world/asia_pacific/south-korea-dismantles-propaganda-loudspeakers-at-border/2018/05/01/21ebaf72-4d08-11e8-85c1-9326c4511033_story.html"
        };

        static async Task Main(string[] args)
        {
            foreach (var url in testURLS)
            {
                var uri = new Uri(url);
                var op = await new Engine(url).DoParseAsync();
                System.IO.Directory.CreateDirectory($"output/{uri.Host}/");
                await System.IO.File.WriteAllTextAsync($"output/{uri.Host}/output.html", op.Content);
            }
        }
    }
}
