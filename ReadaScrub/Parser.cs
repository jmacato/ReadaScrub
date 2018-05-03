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
    public class Engine
    {
        static HttpClient webClient = new HttpClient();
        private string UriString;
        private string[] exceptElems_PTag = new string[] { "P", "A", "IMG", "H1", "H2", "H3", "H4", "H5", "BLOCKQUOTE", "CODE" };
        private string[] attribExceptions = new string[] { "SRC", "HREF" };

        private IHtmlDocument rootDoc;

        public Uri BaseURI { get; private set; }
        public int ParagraphCharacterThreshold { get; set; } = 30;

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
            return System.Text.Encoding.UTF8.GetString(await webClient.GetByteArrayAsync(BaseURI.AbsoluteUri));
        }

        public async Task<Article> DoParseAsync()
        {
            var rootPage = await FetchPage();

            var asParser = new HtmlParser();

            this.rootDoc = await asParser.ParseAsync(rootPage);

            IElement TopCandidate = rootDoc.Body;

             PruneUnlikelyElemments(TopCandidate);
            FirstStagePreprocess(TopCandidate);

            var candidates = ScanForCandidates(TopCandidate)
                            .Where(p => p.Score > 0)
                            .Take(5)
                            .ToList()
                            .OrderByDescending(p => p.Score);

            if (candidates.Count() > 0)
                TopCandidate = candidates?.MinBy(p => p.LinkDensity).Element;

            if (TopCandidate != null)
            {

                _TEMP_RemoveAttribs(TopCandidate);
                _TEMP_TrimAndWSNormAllTextNodes(TopCandidate);
                _TEMP_TransformDanglingTextToPElem(TopCandidate);
                _TEMP_RemoveDanglingWhiteSpace(TopCandidate);
                _TEMP_TrimAndWSNormAllPTags(TopCandidate);
 
                double reductionRate = 1d - ((double)TopCandidate.OuterHtml.Length / rootPage.Length);
                reductionRate *= 100;

                Debug.WriteLine($"--\nReduction Percent: {TopCandidate.OuterHtml.Length}B / {rootPage.Length}B {reductionRate:0.#####}%\n--\n");


                string finalContent;

                var sb = new StringBuilder();
                using (var txtWriter = new StringWriter(sb))
                {
                    TopCandidate.ToHtml(txtWriter, new AngleSharp.XHtml.XhtmlMarkupFormatter());
                    finalContent = sb.ToString();
                    finalContent = Patterns.HTMLComments.Replace(finalContent, "");
                    finalContent = FormatHtmlToXhtml(finalContent);
                }

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
        private void _TEMP_RemoveDanglingWhiteSpace(IElement target)
        {
            foreach (var trgt in target.GetElementsByTagName("*"))
                if (trgt.NodeType == NodeType.Text && Patterns.TotallyWhitespace.IsMatch(trgt.TextContent))
                {
                    trgt.Parent?.RemoveChild(trgt);
                }
        }

        /// <summary>
        /// Replace all elements with all childnodes text to P Tag.
        /// </summary>
        private void _TEMP_RemoveEmptyNodes(IElement target)
        {
            foreach (var trgt in target.GetElementsByTagName("*"))
                if (trgt.ChildNodes.Count() == 0)
                    trgt.Parent?.RemoveChild(trgt);

        }



        private void _TEMP_TrimAndWSNormAllPTags(IElement target)
        {
            foreach (var trgt in target.GetElementsByTagName("*"))
                if (trgt.ChildNodes.All(p => p.NodeType == NodeType.Text))
                    foreach (var trgtCh in trgt.ChildNodes)
                        trgtCh.TextContent = trgtCh.TextContent.RegexTrimNormDecode();

        }




        private void _TEMP_TransformDanglingTextToPElem(IElement target)
        {
            foreach (var child in target.Children.Where(p => p.NodeType == NodeType.Text).ToList())
            {
                var newElem = rootDoc.CreateElement("p");
                newElem.TextContent = Patterns.RegexTrimNormDecode(child.TextContent);

                child.Parent?.ReplaceChild(newElem, child);
                _TEMP_TransformDanglingTextToPElem(child);
            }
        }

        /// <summary>
        /// Trim and normalize whitespace on all text nodes.
        /// </summary>
        /// <param name="target"></param>
        private void _TEMP_TrimAndWSNormAllTextNodes(IElement target)
        {
            foreach (var child in target.Children.Where(p => p.NodeType == NodeType.Text))
            {
                
                child.OuterHtml = Patterns.RegexTrimNormDecode(child.OuterHtml);
                _TEMP_TrimAndWSNormAllTextNodes(child);
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

        private IEnumerable<(double Score, double LinkDensity, IElement Element)> ScanForCandidates(IElement targetElem)
        {
            foreach (var elem in targetElem.GetElementsByTagName("*")
                                           .Where(p => p.TagName.ToUpper() != "BODY")
                                           .Where(p => IsOverPThreshold(p)))
            {
                if (elem.ChildNodes.Count() > 3)
                {
                    var score = ScoreRelevantElementsProportion(elem);
                    var linkDens = ScoreElementForLinkDensity(elem);
                    yield return ((score, linkDens, elem));
                }
            }
        }

        private bool IsOverPThreshold(INode p)
        => p.TextContent.Trim().Length > this.ParagraphCharacterThreshold;

        private bool IsPositiveOrMaybeCandidate(INode p)
        => (Patterns.MaybeCandidates.IsMatch(p.NodeName) ||
            Patterns.PositiveCandidates.IsMatch(p.NodeName));
        static readonly string[] highScorers = new string[] { "P", "BLOCKQUOTE", "CODE" };
        static readonly string[] negativeScorers = new string[] { "A", "IFRAME" };
        static readonly char[] wordSeparators = new char[] { ' ', ',', ';', '.', '!', '"', '(', ')', '?' };
        static readonly StringSplitOptions _sso = StringSplitOptions.RemoveEmptyEntries;
        private double ScoreRelevantElementsProportion(IElement elem)
        {

            var score = 0d;

            // Score the proportions of positively relevant elements.
            score += elem.ChildNodes.Where(p => highScorers.Contains(p.NodeName.ToUpper())).Count();
            score /= elem.ChildNodes.Count();

            var totalWordCount = elem.TextContent.Trim().Split(wordSeparators, _sso).Count();


            Debug.WriteLine($"RE: Word Count : {totalWordCount}");

            // Add word count score in proportion to the high scoring element percentage.
            score *= totalWordCount;

            return score;
        }

        private double ScoreElementForLinkDensity(IElement elem)
        {
            var score = 0d;
            var totalWordCount = elem.TextContent.Trim().Split(wordSeparators, _sso).Count();

            var totalWordCountLinks = elem.GetElementsByTagName("*")
                                          .Where(p => p.TagName.ToUpper() == "A")
                                          .Select(p => p.TextContent.Trim().Split(wordSeparators, _sso).Count())
                                          .Sum();


            var linkDensity = ((float)totalWordCountLinks / (totalWordCount + 1));

            Debug.WriteLine($"Link Density : {linkDensity * 100:0.##}%");

            score *= linkDensity;


            return score;
        }
        private void GloballyRemoveElement(IElement targetElem, string v)
        {
            foreach (var elem in targetElem.GetElementsByTagName(v))
                elem.Parent?.RemoveChild(elem);
        }
    }

}



