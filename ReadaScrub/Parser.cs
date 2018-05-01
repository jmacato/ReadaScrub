using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using MoreLinq;

namespace ReadaScrub
{
    public class Parser
    {
        static HttpClient webClient = new HttpClient();

        private string UriString;
        public Uri BaseURI { get; private set; }
        public int ParagraphCharacterThreshold { get; set; } = 25;

        public Parser(string UriString)
        {
            this.UriString = UriString;

            if (Uri.TryCreate(UriString, UriKind.Absolute, out var res))
                SetBaseURI(res);
            else
                throw new InvalidOperationException($"Invalid URI String '{UriString}'");
        }

        private void SetBaseURI(Uri res)
        {
            this.BaseURI = res;
        }

        public async Task<string> FetchPage()
        {
            return await webClient.GetStringAsync(BaseURI.AbsoluteUri);
        }

        public async Task<Article> DoParseAsync()
        {
            var rootPage = await FetchPage();
            var asParser = new HtmlParser();
            var rootDoc = await asParser.ParseAsync(rootPage);
            var bodyElem = rootDoc.Body;

            GloballyRemoveElement(bodyElem, "script");
            PruneUnlikelyElemments(bodyElem);

            var candidates = ScanForCandidates(bodyElem).ToList();

            var TopCandidate = candidates.MaxBy(p => p.score);


            Debug.WriteLine(bodyElem.OuterHtml);

            return null;
        }

        private void PruneUnlikelyElemments(IHtmlElement targetElem)
        {
            foreach (var elem in targetElem.GetElementsByTagName("*"))
            {
                if (Patterns.UnlikelyCandidates.IsMatch(elem.TagName)
                    && !Patterns.MaybeCandidates.IsMatch(elem.TagName))
                {
                    elem.Parent?.RemoveChild(elem);
                }
            }
        }

        private IEnumerable<(double score, IElement target)> ScanForCandidates(IElement targetElem)
        {
            foreach (var elem in targetElem.GetElementsByTagName("*"))
            {
                var paragraphQuery = elem.ChildNodes
                                         .Where(p => IsOverPThreshold(p));

                if (paragraphQuery.Any() && paragraphQuery.Count() > 1)
                {
                    var score = ScoreElementForContent(elem);
                    yield return ((score, elem));
                }
            }
        }

        private bool IsOverPThreshold(INode p)
        => p.TextContent.Trim().Length > this.ParagraphCharacterThreshold;

        private bool IsPositiveOrMaybeCandidate(INode p)
        => (Patterns.MaybeCandidates.IsMatch(p.NodeName) ||
            Patterns.PositiveCandidates.IsMatch(p.NodeName));


        private double ScoreElementForContent(IElement elem)
        {
            var score = 0d;
            score += elem.ChildNodes.Where(p => p.NodeName.ToUpper() == "P").Count();
            return score;
        }

        private void GloballyRemoveElement(IElement targetElem, string v)
        {
            foreach (var elem in targetElem.GetElementsByTagName(v))
                elem.Parent?.RemoveChild(elem);
        }
    }

}





