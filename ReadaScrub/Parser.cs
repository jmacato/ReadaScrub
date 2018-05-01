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



            IElement TopCandidate = rootDoc.Body;


            FirstStagePreprocess(TopCandidate);
            PruneUnlikelyElemments(TopCandidate);

            var candidates = ScanForCandidates(TopCandidate)
                            .OrderByDescending(p => p.Score)
                            .ToList();

            TopCandidate = candidates
            .Where(p => p.Element.TagName.ToUpper() != "BODY")
            .MaxBy(p => p.Score).Element;

            _TEMP_RemoveAllAttribs(TopCandidate);

            Debug.WriteLine(TopCandidate.InnerHtml);



            return null;
        }

        private void _TEMP_RemoveAllAttribs(IElement topCandidate)
        {
            foreach (var attr in topCandidate.Attributes.ToList())
            {
                topCandidate.Attributes.RemoveNamedItem(attr.Name);
            }
            foreach (var child in topCandidate.Children)
            {
                _TEMP_RemoveAllAttribs(child);
            }
        }

        private void FirstStagePreprocess(IElement bodyElem)
        {
            TransferChildAndRemove(bodyElem, "form");
            TransferChildAndRemove(bodyElem, "script");
        }

        /// <summary>
        /// Deletes the target element and transfers its children to 
        /// its ancestor element
        /// </summary> 
        private void TransferChildAndRemove(IElement root, string targetElemName)
        {
            foreach (var elem in root.GetElementsByTagName(targetElemName))
            {
                foreach (var child in elem.ChildNodes.ToList())
                {
                    elem.Parent?.AppendChild(child);
                }
                elem.Parent?.RemoveChild(elem);

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
            var highScorers = new string[] { "P", "SPAN" };

            var l1 = elem.GetElementsByTagName("*")
                 .Where(p => IsOverPThreshold(p))
                 .ToList();

            if (elem.ChildNodes.Count() < 2) return 0;

            var score = 0d;

            score += l1.Where(p => highScorers.Contains(p.NodeName.ToUpper())).Count();
            score /= elem.ChildNodes.Count();

            // score *= 47;

            // Add score for having elements with text content
            // over the paragraph threshold.
            // score += l1
            //         .Count();

            // //Add score for having elements with apparently meaningful words
            // score *= l1
            //         .Select(p =>
            //         {


            //             var baseWordScore = Patterns.NormalizeWS.Replace(p.TextContent, " ")
            //                      .Split(' ')
            //                      .Select(word => word.Length > 7)
            //                      .Count();

            //             if (highScorers.Contains(p.TagName.ToUpper()))
            //             {
            //                 baseWordScore *= 6;
            //             }
            //             else
            //             {
            //                 baseWordScore /= 6;
            //             }

            //             return baseWordScore;
            //         })
            //         .Sum();

            // if (Patterns.PositiveCandidates.IsMatch(elem.TagName))
            // {
            //     score *= 1.5;
            // }
            // else
            // {
            //     score *= 0.8;
            // }


            //     var links = l1
            //             .Where(p => p.Attributes
            //                          .Select(x => x.Name.ToUpper()).Contains("HREF"));

            //     var linkCount = links.Count();
            //     var totalChildElems = l1.Count();
            //     var linkDensity = 1 - (linkCount / totalChildElems);

            //     score *= linkDensity;

            //     if (Patterns.PositiveCandidates.IsMatch(elem.TagName) ||
            //    Patterns.PositiveCandidates.IsMatch(String.Join(" ", elem.ClassList)))
            //     {
            //         score *= 1.5;
            //     }

            return score;
        }

        private void GloballyRemoveElement(IElement targetElem, string v)
        {
            foreach (var elem in targetElem.GetElementsByTagName(v))
                elem.Parent?.RemoveChild(elem);
        }
    }

}





