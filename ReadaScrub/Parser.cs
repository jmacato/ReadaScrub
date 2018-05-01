#define FAKE_BROWSER

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
        string[] exceptElems = new string[] { "P", "A", "IMG" };
        string[] attribExceptions = new string[] { "SRC", "HREF" };
        private IHtmlDocument rootDoc;

        public Parser(string UriString)
        {
            this.UriString = UriString;

            if (Uri.TryCreate(UriString, UriKind.Absolute, out var res))
                SetBaseURI(res);
            else
                throw new InvalidOperationException($"Invalid URI String '{UriString}'");

#if FAKE_BROWSER
            webClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla (Chrome;Windows)");
            webClient.DefaultRequestHeaders.TryAddWithoutValidation("Referrer", "https://www.google.com/");
#endif

        }

        private void SetBaseURI(Uri res)
        {
            this.BaseURI = res;
        }

        public async Task<string> FetchPage()
        {
            return System.Text.Encoding.UTF8.GetString(await webClient.GetByteArrayAsync(BaseURI.AbsoluteUri));
        }

        public async Task<Article> DoParseAsync()
        {
            var rootPage = await FetchPage();

            var asParser = new HtmlParser();

            this.rootDoc = await asParser.ParseAsync(rootPage);

            IElement TopCandidate = rootDoc.Body;

            PruneUnlikelyElemments(TopCandidate);
            PruneNegativeElemments(TopCandidate);
            FirstStagePreprocess(TopCandidate);

            var candidates = ScanForCandidates(TopCandidate)
                            .OrderByDescending(p => p.Score)
                            .ToList();

            TopCandidate = candidates.MaxBy(p => p.Score).Element;

            var parseSuccess = false;

            if (TopCandidate != null)
            {
                _TEMP_RemoveAttribs(TopCandidate);
                _TEMP_TrimAndWSNormAllTextContent(TopCandidate);
                _TEMP_TransformDanglingTextToPElem(TopCandidate);
                _TEMP_ElemsWithAllTextOnlyToPTag(TopCandidate);
                _TEMP_RemoveDanglingWhiteSpace(TopCandidate);

                double reductionRate = 1d - ((double)TopCandidate.OuterHtml.Length / rootPage.Length);
                reductionRate *= 100;

                Debug.WriteLine($"--\nReduction Percent: {TopCandidate.OuterHtml.Length}B / {rootPage.Length}B {reductionRate:0.#####}%\n--\n");

                parseSuccess = true;
            }

            Debug.WriteLine(TopCandidate.OuterHtml);

            return new Article()
            {
                Success = parseSuccess,
                Content = TopCandidate.OuterHtml
            };
        }

        /// <summary>
        /// Remove all purely whitespace nodes.
        /// </summary>
        private void _TEMP_RemoveDanglingWhiteSpace(INode target)
        {
            foreach (var trgt in target.ChildNodes.ToList())
                if (trgt.NodeType == NodeType.Text && Patterns.Whitespace.IsMatch(trgt.TextContent))
                {
                    trgt.Parent?.RemoveChild(trgt);
                    _TEMP_RemoveDanglingWhiteSpace(trgt);
                }
        }

        /// <summary>
        /// Replace all elements with all childnodes text to P Tag.
        /// </summary>
        private void _TEMP_ElemsWithAllTextOnlyToPTag(IElement target)
        {
            foreach (var trgt in target.GetElementsByTagName("*")
                                       .Where(p => !exceptElems.Any(x => x == p.TagName.ToUpper()))
                                       .ToList())
                if (trgt.Children.All(p => p.NodeType == NodeType.Text))
                {
                    var targetText = Patterns.RegexTrimAndNormalize(trgt.TextContent);

                    if (targetText.Length > 0)
                    {
                        var newElem = rootDoc.CreateElement("p");
                        newElem.TextContent = targetText;
                        trgt.Parent?.AppendChild(newElem);
                    }
                    trgt.Parent?.RemoveChild(trgt);
                }
        }

        private void _TEMP_TransformDanglingTextToPElem(IElement target)
        {
            foreach (var child in target.Children.Where(p => p.NodeType == NodeType.Text).ToList())
            {
                var newElem = rootDoc.CreateElement("p");
                newElem.TextContent = Patterns.RegexTrimAndNormalize(child.TextContent);

                child.Parent?.AppendChild(newElem);
                child.Parent?.RemoveChild(child);
                _TEMP_TransformDanglingTextToPElem(child);
            }
        }

        /// <summary>
        /// Trim and normalize whitespace on all text nodes.
        /// </summary>
        /// <param name="target"></param>
        private void _TEMP_TrimAndWSNormAllTextContent(IElement target)
        {
            foreach (var child in target.Children.Where(p => p.NodeType == NodeType.Text))
            {
                child.TextContent = Patterns.RegexTrimAndNormalize(child.TextContent);
                _TEMP_TrimAndWSNormAllTextContent(child);
            }
        }

        private void _TEMP_RemoveAttribs(IElement target)
        {
            foreach (var attr in target.Attributes.ToList())
            {
                if (!attribExceptions.Any(p => p.ToUpper() == attr.Name.ToUpper()))
                    target.Attributes.RemoveNamedItem(attr.Name);
            }
            foreach (var child in target.Children)
            {
                _TEMP_RemoveAttribs(child);
            }
        }

        private void FirstStagePreprocess(IElement target)
        {
            TransferChildAndRemove(target, "form");
            TransferChildAndRemove(target, "script", true);
            TransferChildAndRemove(target, "noscript", true);
        }

        /// <summary>
        /// Deletes the target element and transfers its children to 
        /// its ancestor element
        /// </summary> 
        private void TransferChildAndRemove(IElement root, string targetElemName, bool RemoveChildTextElems = false)
        {

            foreach (var elem in root.GetElementsByTagName(targetElemName))
            {

                if (elem.ChildNodes.Any(p => p.NodeType != NodeType.Text))
                {
                    foreach (var child in elem.ChildNodes.ToList())
                    {
                        if (RemoveChildTextElems)
                        {
                            if (child.NodeType == NodeType.Text)
                                continue;
                        }
                        elem.Parent.AppendChild(child);
                    }
                }

                elem.Parent?.RemoveChild(elem);

            }
        }
        private void PruneNegativeElemments(IElement targetElem)
        {
            foreach (var elem in targetElem.GetElementsByTagName("*"))
            {
                if (Patterns.NegativeCandidates.IsMatch(elem.TagName) ||
                    Patterns.NegativeCandidates.IsMatch(elem.Id ?? " ") ||
                    Patterns.NegativeCandidates.IsMatch(elem.ClassList.ToDelimitedString(" ") ?? " "))
                {
                    elem.Parent?.RemoveChild(elem);
                }
            }
        }

        private void PruneUnlikelyElemments(IElement targetElem)
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

        private IEnumerable<(double Score, IElement Element)> ScanForCandidates(IElement targetElem)
        {
            foreach (var elem in targetElem.GetElementsByTagName("*")
                                           .Where(p => p.TagName.ToUpper() != "BODY"))
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
            var highScorers = new string[] { "P", "SPAN" };

            var l1 = elem.ChildNodes
                 .Where(p => IsOverPThreshold(p))
                 .ToList();

            if (elem.ChildNodes.Count() < 2) return 0;

            var score = 0d;

            score += l1.Where(p => highScorers.Contains(p.NodeName.ToUpper())).Count();

            score /= elem.ChildNodes.Count();

            score *= Patterns.NormalizeWS.Replace(elem.TextContent.Trim(), " ").Split(' ').Where(p => p.Length > 5).Count();



            return score;
        }

        private void GloballyRemoveElement(IElement targetElem, string v)
        {
            foreach (var elem in targetElem.GetElementsByTagName(v))
                elem.Parent?.RemoveChild(elem);
        }
    }

}



