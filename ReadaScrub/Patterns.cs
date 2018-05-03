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
using System.Text.RegularExpressions;
using System.Web;

namespace ReadaScrub
{
    public static class StringExtensions
    {
        public static string RegexTrimAndNormalize(this string input)
        {
            var res = input;
            res = res.Trim();
            res = Regex.Replace(res, @"^\s+", "");
            res = Regex.Replace(res, @"\s+$", "");
            res = Engine.NormalizeWS.Replace(res, " ");
            return res;
        }
    }

    public partial class Engine
    {
        public static RegexOptions _regexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        public static Regex UnlikelyCandidates = new Regex("style|sidebar|aside|banner|breadcrumbs|combx|comment|community|cover-wrap|disqus|extra|foot|header|legends|menu|related|remark|replies|rss|shoutbox|sidebar|skyscraper|social|sponsor|supplemental|ad-break|agegate|pagination|pager|popup|yom-remote", _regexOptions);
        public static Regex MaybeCandidates = new Regex("and|article|body|column|main|shadow|section", _regexOptions);
        public static Regex PositiveCandidates = new Regex("article|body|content|entry|hentry|h-entry|main|page|pagination|post|text|blog|story", _regexOptions);
        public static Regex NegativeCandidates = new Regex("hidden|^hid$| hid$| hid |^hid |banner|combx|comment|com-|contact|foot|footer|footnote|masthead|media|meta|outbrain|promo|related|scroll|share|shoutbox|sidebar|skyscraper|sponsor|shopping|tags|tool|widget", _regexOptions);
        public static Regex ExtraneousCandidates = new Regex("print|archive|comment|discuss|e[\\-]?mail|share|reply|all|login|sign|single|utility", _regexOptions);
        public static Regex ByLineCandidates = new Regex("byline|author|dateline|writtenby|p-author", _regexOptions);
        public static Regex NormalizeWS = new Regex("\\s{2,}", _regexOptions | RegexOptions.Multiline);
        public static Regex Videos = new Regex("\\/\\/(www\\.)?(dailymotion|youtube|youtube-nocookie|player\\.vimeo)\\.com", _regexOptions);
        public static Regex NextLink = new Regex("(next|weiter|continue|>([^\\|]|$)|»([^\\|]|$))", _regexOptions);
        public static Regex PrevLink = new Regex("(prev|earl|old|new|<|«)", _regexOptions);
        public static Regex TotallyWhitespace = new Regex("^\\s*$", _regexOptions);
        public static Regex Whitespace = new Regex("\\s+", _regexOptions);
        public static Regex HTMLComments = new Regex("<!--[\\s\\S]*?(?:-->)?<!---+>?|<!(?![dD][oO][cC][tT][yY][pP][eE]|\\[CDATA\\[)[^>]*>?|<[?][^>]*>?", _regexOptions);

    }

}