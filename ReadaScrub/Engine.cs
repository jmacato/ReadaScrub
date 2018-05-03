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

#define FAKE_BROWSER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using MoreLinq;

namespace ReadaScrub
{
    public partial class Engine
    {
        static HttpClient webClient = new HttpClient();
        private string UriString;
        private string[] attribExceptions = new string[] { "SRC", "HREF" };
        private IHtmlDocument rootDoc;
        public Uri BaseURI { get; private set; }
        public int ParagraphCharacterThreshold { get; set; } = 30;
        private bool IsOverPThreshold(IElement p)
        => p.TextContent.Trim().Length > this.ParagraphCharacterThreshold;
        private bool IsPositiveOrMaybeCandidate(IElement p)
        => (MaybeCandidates.IsMatch(p.NodeName) ||
            PositiveCandidates.IsMatch(p.NodeName));
        private bool IsEmptyElement(IElement p) => p.Attributes.Count() == 0 &&
                    p.Children.Count() == 0 &&
                    p.InnerHtml.RegexTrimAndNormalize().Length == 0;
        static readonly string[] highScorers = new string[] { "P", "A", "IMG", "H1", "H2", "H3", "H4", "H5", "BLOCKQUOTE", "CODE" };
        static readonly char[] wordSeparators = new char[] { ' ', ',', ';', '.', '!', '"', '(', ')', '?' };
        static readonly StringSplitOptions _sso = StringSplitOptions.RemoveEmptyEntries;

        public Engine(string UriString)
        {
            this.UriString = UriString;

            if (Uri.TryCreate(UriString, UriKind.Absolute, out var res))
                SetBaseURI(res);
            else
                throw new InvalidOperationException($"Invalid URI String '{UriString}'");

#if FAKE_BROWSER
            webClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla (Chrome;Windows)");
            webClient.DefaultRequestHeaders.TryAddWithoutValidation("Referrer", "https://amp.google.com/");
#endif

        }

        private void SetBaseURI(Uri res)
        {
            this.BaseURI = res;
        }

        public async Task<string> FetchPage()
        {
            // Force the encoding to UTF8 
            try
            {
                return await webClient.GetStringAsync(BaseURI.AbsoluteUri);

            }
            catch (Exception)
            {
                return Encoding.UTF8.GetString(await webClient.GetByteArrayAsync(BaseURI.AbsoluteUri));

            }
        }

        public async Task<Article> DoParseAsync()
        {
            var rootPage = await FetchPage();
            var asParser = new HtmlParser();
            this.rootDoc = await asParser.ParseAsync(rootPage);

            IElement TopCandidate = null, FirstCandidate = rootDoc.Body;

            PruneUnlikelyElemments(FirstCandidate);
            FirstStagePreprocess(FirstCandidate);

            var candidates = ScanForCandidates(FirstCandidate);

            if (candidates.Count() > 0)
            {
                candidates = candidates
                                .OrderByDescending(p => p.Score)
                                .Take(10)
                                .OrderByDescending(p => p
                                                         .Element
                                                         .TextContent
                                                         .RegexTrimAndNormalize()
                                                         .Split(wordSeparators, _sso)
                                                         .Where(x => x.Length > 4)
                                                         .Count())
                                .Take(2)
                                .OrderBy(p => p.LinkDensity);

            }
            if (candidates.Count() > 0)
                TopCandidate = candidates?.FirstOrDefault().Element;

            if (TopCandidate != null)
            {
                PostProcessCandidate(TopCandidate);

                double reductionRate = 1d - ((double)TopCandidate.OuterHtml.Length / rootPage.Length);
                Debug.WriteLine($"--\nReduction Percent: {TopCandidate.OuterHtml.Length / 1024:0.##}KB / {rootPage.Length / 1024:0.##}KB {reductionRate * 100:0.#####}%\n--\n");
 
                string finalContent = FormatToXhtml(TopCandidate); 
                return new Article()
                {
                    Uri = BaseURI,
                    Success = true,
                    Content = finalContent
                };
            }

            return new Article()
            {
                Success = false
            };

        }

        private void PostProcessCandidate(IElement TopCandidate)
        {
            PostProcessRemoveAttribs(TopCandidate);
            PostProcessTrimAndWSNormAllTextNodes(TopCandidate);
            PostProcessTransformDanglingTextToPElem(TopCandidate);
            PostProcessRemoveDanglingWhiteSpace(TopCandidate);
            PostProcessTrimAndWSNormAllPTags(TopCandidate);
            PostProcessRemoveEmptyNodes(TopCandidate);

        }

        private string FormatToXhtml(IElement topCandidate)
        {
            var sb = new StringBuilder();
            using (var txtWriter = new StringWriter(sb))
            {
                topCandidate.ToHtml(txtWriter, new AngleSharp.XHtml.XhtmlMarkupFormatter());
                var finalContent = sb.ToString();
                finalContent = HTMLComments.Replace(finalContent, "");
                return FormatHtmlToXhtml(finalContent);
            }
        }

        string FormatHtmlToXhtml(string input)
        {
            try
            {
                XDocument doc = XDocument.Parse(input);
                return doc.ToString();
            }
            catch (Exception)
            {
                // Handle and throw if fatal exception here; don't just ignore them
                return input;
            }
        }

        /// <summary>
        /// Remove all purely whitespace nodes.
        /// </summary>
        private void PostProcessRemoveDanglingWhiteSpace(IElement target)
        {
            BreadthFirstDo(target, (child) =>
            {
                if (child.NodeType == NodeType.Text && TotallyWhitespace.IsMatch(child.OuterHtml))
                    child.Parent?.RemoveChild(child);
            });
        }

        private void BreadthFirstDo(IElement target, Action<IElement> predicate)
        {
            predicate(target);
            foreach (var kid in target.Children)
            {
                predicate(kid);
                foreach (var grandkid in kid.Children)
                    BreadthFirstDo(grandkid, predicate);
            }
        }

        /// <summary>
        /// Replace all elements without children
        /// </summary>
        private void PostProcessRemoveEmptyNodes(IElement target)
        {
            do
            {
                foreach (var child in target.GetElementsByTagName("*"))
                    if (IsEmptyElement(child))
                        child.Parent?.RemoveChild(child);

            }
            while (target.GetElementsByTagName("*").Any(p => IsEmptyElement(p)));
        }



        /// <summary>
        /// Normalize Text inside P tags.
        /// </summary>
        /// <param name="target"></param>
        private void PostProcessTrimAndWSNormAllPTags(IElement target)
        {
            foreach (var trgt in target.GetElementsByTagName("*"))
                if (trgt.Children.All(p => p.NodeType == NodeType.Text))
                    foreach (var trgtCh in trgt.Children)
                        trgtCh.TextContent = trgtCh.TextContent.RegexTrimAndNormalize();

        }

        private void PostProcessTransformDanglingTextToPElem(IElement target)
        {
            foreach (var child in target.Children.Where(p => p.NodeType == NodeType.Text).ToList())
            {
                var newElem = rootDoc.CreateElement("p");
                newElem.TextContent = child.TextContent.RegexTrimAndNormalize();

                child.Parent?.ReplaceChild(newElem, child);
                PostProcessTransformDanglingTextToPElem(child);
            }
        }

        /// <summary>
        /// Trim and normalize whitespace on all text nodes.
        /// </summary>
        /// <param name="target"></param>
        private void PostProcessTrimAndWSNormAllTextNodes(IElement target)
        {
            BreadthFirstDo(target, (child) =>
            {
                if (child.NodeType == NodeType.Text)
                {
                    child.OuterHtml = child.OuterHtml.RegexTrimAndNormalize();
                }
            });
        }

        private void PostProcessRemoveAttribs(IElement target)
        {
            BreadthFirstDo(target, (child) =>
            {
                foreach (var attr in child.Attributes.ToList())
                    if (!attribExceptions.Any(p => p.ToUpper() == attr.Name.ToUpper()))
                        child.Attributes.RemoveNamedItem(attr.Name);
            });
        }

        private void FirstStagePreprocess(IElement target)
        {
            TransferChildrenToAncestorAndRemove(target, "form");
            TransferChildrenToAncestorAndRemove(target, "script", true);
            TransferChildrenToAncestorAndRemove(target, "noscript", true);
         }

        /// <summary>
        /// Deletes the target element and transfers its children to 
        /// its ancestor element
        /// </summary> 
        private void TransferChildrenToAncestorAndRemove(IElement root, string targetElemName, bool RemoveChildTextElems = false)
        {
            foreach (var elem in root.GetElementsByTagName(targetElemName))
            {
                if (elem.Children.Any(p => p.NodeType != NodeType.Text))
                {
                    foreach (var child in elem.Children.ToList())
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

        private void PruneUnlikelyElemments(IElement targetElem)
        {
            foreach (var elem in targetElem.GetElementsByTagName("*"))
            {
                if (UnlikelyCandidates.IsMatch(elem.TagName)
                    && !MaybeCandidates.IsMatch(elem.TagName))
                {
                    elem.Parent?.RemoveChild(elem);
                }
            }
        }

        private IEnumerable<(double Score, double LinkDensity, IElement Element)> ScanForCandidates(IElement targetElem)
        {
            foreach (var elem in targetElem.GetElementsByTagName("*")
                                           .Where(p => p.TagName.ToUpper() != "BODY")
                                           .Where(p => IsOverPThreshold(p)))
            {
                if (elem.Children.Count() > 3)
                {
                    var score = ScoreRelevantElementsProportion(elem);
                    var linkDens = ScoreElementForLinkDensity(elem);
                    yield return ((score, linkDens, elem));
                }
            }
        }
        private double ScoreRelevantElementsProportion(IElement elem)
        {

            var score = 0d;

            // Score the proportions of positively relevant elements.
            score += elem.Children.Where(p => highScorers.Contains(p.NodeName.ToUpper())).Count();
            score /= elem.Children.Count();

            var totalWordCount = elem.TextContent.Trim().Split(wordSeparators, _sso).Count();


            Debug.WriteLine($"RE: Word Count : {totalWordCount}");

            // Add word count score in proportion to the high scoring element percentage.
            score *= totalWordCount;

            return score;
        }

        private double ScoreElementForLinkDensity(IElement elem)
        {

            var totalWordCount = elem.TextContent.Trim().Split(wordSeparators, _sso).Count();

            var totalWordCountLinks = elem.GetElementsByTagName("*")
                                          .Where(p => p.TagName.ToUpper() == "A")
                                          .Select(p => p.TextContent.Trim().Split(wordSeparators, _sso).Count())
                                          .Sum();


            var linkDensity = ((float)totalWordCountLinks / (totalWordCount + 1));

            Debug.WriteLine($"Link Density : {linkDensity * 100:0.##}%");

            return linkDensity;

        }
        private void GloballyRemoveElement(IElement targetElem, string v)
        {
            foreach (var elem in targetElem.GetElementsByTagName(v))
                elem.Parent?.RemoveChild(elem);
        }

    }
}