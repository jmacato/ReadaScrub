using System;
using System.Text.RegularExpressions;


namespace ReadaScrub
{
    public class Patterns
    {
        public static RegexOptions _regexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ECMAScript | RegexOptions.IgnoreCase;
        public static Regex UnlikelyCandidates = new Regex("banner|breadcrumbs|combx|comment|community|cover-wrap|disqus|extra|foot|header|legends|menu|related|remark|replies|rss|shoutbox|sidebar|skyscraper|social|sponsor|supplemental|ad-break|agegate|pagination|pager|popup|yom-remote", _regexOptions);
        public static Regex MaybeCandidates = new Regex("and|article|body|column|main|shadow", _regexOptions);
        public static Regex PositiveCandidates = new Regex("article|body|content|entry|hentry|h-entry|main|page|pagination|post|text|blog|story", _regexOptions);
        public static Regex NegativeCandidates = new Regex("hidden|^hid$| hid$| hid |^hid |banner|combx|comment|com-|contact|foot|footer|footnote|masthead|media|meta|outbrain|promo|related|scroll|share|shoutbox|sidebar|skyscraper|sponsor|shopping|tags|tool|widget", _regexOptions);
        public static Regex ExtraneousCandidates = new Regex("print|archive|comment|discuss|e[\\-]?mail|share|reply|all|login|sign|single|utility", _regexOptions);
        public static Regex ByLineCandidates = new Regex("byline|author|dateline|writtenby|p-author", _regexOptions);
        public static Regex ReplaceFonts = new Regex("<(\\/?)font[^>]*>", _regexOptions | RegexOptions.Multiline);
        public static Regex Normalize = new Regex("\\s{2,}", _regexOptions | RegexOptions.Multiline);
        public static Regex Videos = new Regex("\\/\\/(www\\.)?(dailymotion|youtube|youtube-nocookie|player\\.vimeo)\\.com", _regexOptions);
        public static Regex NextLink = new Regex("(next|weiter|continue|>([^\\|]|$)|»([^\\|]|$))", _regexOptions;
        public static Regex PrevLink = new Regex("(prev|earl|old|new|<|«)", _regexOptions);
        public static Regex Whitespace = new Regex("^\\s*$", _regexOptions);
        public static Regex HasContent = new Regex("^\\S$*", _regexOptions);

        public static Regex TitleRegex1 = new Regex(@" [\|\-\\\/>»] ", _regexOptions);
        public static Regex TitleRegex2 = new Regex(@" [\\\/>»] ", _regexOptions);
        public static Regex TitleRegex3 = new Regex(@" (.*)[\|\-\\\/>»].*", _regexOptions | RegexOptions.Multiline);
        public static Regex TitleRegex4 = new Regex(@"[^\|\-\\\/>»] *[\|\-\\\/>»](.*) ", _regexOptions | RegexOptions.Multiline);
        public static Regex TitleRegex5 = new Regex(@"[\|\-\\\/>»] +", _regexOptions | RegexOptions.Multiline);

        // Match "description", or Twitter's "twitter:description" (Cards)
        // in name attribute.
        public static Regex namePattern = new Regex(@"^\s*((twitter)\s*:\s*)?(description|title)\s*$", _regexOptions);

        // Match Facebook's Open Graph title & description properties. 
        public static Regex propertyPattern = new Regex(@"^\s*og\s*:\s*(description|title)\s*$", _regexOptions);


    }

}