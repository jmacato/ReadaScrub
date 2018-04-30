using System;
using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;

namespace ReadaScrub
{



    public class Parser
    {

        [Flags]
        enum ParserFlags
        {
            FLAG_STRIP_UNLIKELYS,
            FLAG_WEIGHT_CLASSES,
            FLAG_CLEAN_CONDITIONALLY
        }

        /// <summary>
        /// https://developer.mozilla.org/en-US/docs/Web/API/Node/nodeType
        /// </summary>
        int ELEMENT_NODE = 1;

        int TEXT_NODE = 3;

        /// <summary>
        /// Max number of nodes supported by this parser. Default = 0 (no limit)
        /// </summary>
        int DEFAULT_MAX_ELEMS_TO_PARSE = 0;

        /// <summary>
        /// The number of top candidates to consider when analysing how
        /// tight the competition is among candidates.
        /// </summary>
        int DEFAULT_N_TOP_CANDIDATES = 5;

        /// <summary>
        /// The default number of chars an article must have in order to return a result
        /// </summary>
        int DEFAULT_CHAR_THRESHOLD = 500;


        /// <summary>
        /// Element tags to score by default.
        /// </summary>
        string[] DEFAULT_TAGS_TO_SCORE = "section,h2,h3,h4,h5,h6,p,td,pre".ToUpper().Split(',');

        string[] DIV_TO_P_ELEMS = new string[] { "A", "BLOCKQUOTE", "DL", "DIV", "IMG", "OL", "P", "PRE", "TABLE", "UL", "SELECT" };

        string[] ALTER_TO_DIV_EXCEPTIONS = new string[] { "DIV", "ARTICLE", "SECTION", "P" };

        string[] PRESENTATIONAL_ATTRIBUTES = new string[] { "align", "background", "bgcolor", "border", "cellpadding", "cellspacing", "frame", "hspace", "rules", "style", "valign", "vspace" };

        string[] DEPRECATED_SIZE_ATTRIBUTE_ELEMS = new string[] { "TABLE", "TH", "TD", "HR", "PRE" };


        /// <summary>
        /// These are the classes that readability sets itself.
        /// </summary>
        string[] CLASSES_TO_PRESERVE = new string[] { "readability-styled", "page" };




        /**
         * Run any post-process modifications to article content as necessary.
         *
         * @param Element
         * @return void
        **/
        void _postProcessContent(HtmlNode articleContent)
        {
            // Readability cannot open relative uris so we convert them to absolute uris.
            _fixRelativeUris(articleContent);

            // Remove classes.
            _cleanClasses(articleContent);
        }


        /**
         * Iterates over a NodeList, calls `filterFn` for each node and removes node
         * if function returned `true`.
         *
         * If function is not passed, removes all the nodes in node list.
         * 
         */
        void _removeNodes(IEnumerable<HtmlNode> nodeList, Func<HtmlNode, bool> filterFn)
        {
            foreach (var node in nodeList)
            {
                var parentNode = node.ParentNode;
                if (parentNode != null)
                {
                    if (filterFn(node))
                    {
                        parentNode.RemoveChild(node);
                    }
                }
            }
        }

        /**
        * Iterates over a HtmlNodeCollection, and calls _setNodeTag for each node.
        *
        * @param HtmlNodeCollection nodeList The nodes to operate on
        * @param String newTagName the new tag name to use
        * @return void
        */
        void _replaceNodeTags(HtmlNodeCollection nodeList, string newTagName)
        {
            for (var i = nodeList.Count - 1; i >= 0; i--)
            {
                var node = nodeList[i];
                // _setNodeTag(node, newTagName);
                node.Name = newTagName;

            }
        }

        List<HtmlNode> _concatHtmlNodeCollections(params HtmlNodeCollection[] lists) => lists.SelectMany(p => p).ToList();


        private string Html;
        private Uri BaseUri;
        HtmlDocument root;
        HtmlNode doc => doc;

        public void Parse(string Html, Uri baseUri)
        {
            this.Html = Html;
            this.BaseUri = baseUri;
            root = new HtmlDocument();
            root.LoadHtml(Html);
        }


        /// <summary>

        ///  Removes the class="" attribute from every element in the given
        ///  subtree, except those that match CLASSES_TO_PRESERVE and
        ///  the classesToPreserve array from the options object.

        /// </summary>
        void _cleanClasses(HtmlNode node)
        {
            var classesToPreserve = CLASSES_TO_PRESERVE;
            var classNameV1 = node.Attributes["class"].Value;
            var classNameV2 = Patterns.Whitespace.Split(classNameV1)
                              .Where(p => CLASSES_TO_PRESERVE.Any(x => x == p))
                              .ToList();
            var className = String.Join(" ", classNameV2);


            if (className?.Length > 0)
            {
                node.Attributes["class"].Value = className;
            }
            else
            {
                node.Attributes.Remove("class");
            }

            foreach (var child in node.ChildNodes)
            {
                this._cleanClasses(child);
            }
        }


        /// <summary>
        /// Converts each &lt;a&gt; and &lt;img&gt; uri in the given element to an absolute URI,
        /// ignoring #ref URIs.
        /// </summary> 
        void _fixRelativeUris(HtmlNode articleContent)
        {
            var baseURI = new Uri("http://" + BaseUri.Host);
            var documentURI = BaseUri;

            Uri toAbsoluteURI(Uri uri)
            {
                // Leave hash links alone if the base URI matches the document URI:
                if (baseURI == documentURI && uri.AbsoluteUri[0] == '#')
                {
                    return uri;
                }
                // Otherwise, resolve against base URI:
                try
                {
                    return new Uri(baseURI, uri);
                }
                catch (Exception)
                {
                    // Something went wrong, just return the original:
                }
                return uri;
            }

            var links = articleContent.SelectNodes("a");
            foreach (var link in links)
            {
                var href = link.Attributes["href"];
                if (href != null)
                {
                    // Replace links with javascript: URIs with text content, since
                    // they won't work after scripts have been removed from the page.
                    if (href.Value.StartsWith("javascript:"))
                    {
                        var text = root.CreateElement(link.InnerText);
                        link.ParentNode.ReplaceChild(text, link);
                    }
                    else
                    {
                        link.Attributes["href"].Value = toAbsoluteURI(new Uri(href.Value)).AbsoluteUri;
                    }
                }
            }

            var imgs = articleContent.SelectNodes("img");
            foreach (var img in imgs)
            {
                var src = img.Attributes["src"];
                if (src != null)
                {
                    img.Attributes["src"].Value = toAbsoluteURI(new Uri(src.Value)).AbsoluteUri;
                }
            }
        }


        int wordCount(string str)
        {
            return Patterns.Whitespace.Split(str).Length;
        }

        /// <summary>
        /// Get the article title as an H1.
        /// </summary>
        string _getArticleTitle()
        {

            var curTitle = "";
            var origTitle = "";

            var titleElem = doc.SelectSingleNode("title");

            if (titleElem != null)
                curTitle = origTitle = titleElem.InnerText;

            var titleHadHierarchicalSeparators = false;

            // If there's a separator in the title, first remove the final part
            if (Patterns.TitleRegex1.Match(curTitle).Success)
            {
                titleHadHierarchicalSeparators = Patterns.TitleRegex2.Match(curTitle).Success;

                curTitle = Patterns.TitleRegex3.Replace(origTitle, "$1");

                // If the resulting title is too short (3 words or fewer), remove
                // the first part instead:
                if (wordCount(curTitle) < 3)
                    curTitle = Patterns.TitleRegex4.Replace(origTitle, "$1");
            }
            else if (curTitle.Contains(": "))
            {
                // Check if we have an heading containing this exact string, so we
                // could assume it's the full title.
                var headings = this._concatHtmlNodeCollections(
                  doc.SelectNodes("h1"),
                  doc.SelectNodes("h2")
                );

                var match = headings.Where(p => p.InnerText == curTitle).Any();

                // If we don't, let's extract the title out of the original title string.
                if (!match)
                {
                    curTitle = origTitle.Substring(origTitle.LastIndexOf(':') + 1);

                    // If the title is now too short, try the first colon instead:
                    if (wordCount(curTitle) < 3)
                    {
                        curTitle = origTitle.Substring(origTitle.IndexOf(':') + 1);
                        // But if we have too many words before the colon there's something weird
                        // with the titles and the H tags so let's just use the original title instead
                    }
                    else if (wordCount(origTitle.Substring(0, origTitle.IndexOf(':'))) > 5)
                    {
                        curTitle = origTitle;
                    }
                }
            }
            else if (curTitle.Length > 150 || curTitle.Length < 15)
            {
                var hOnes = doc.SelectSingleNode("h1");

                if (hOnes != null)
                    curTitle = hOnes.InnerText;
            }

            curTitle = curTitle.Trim();

            // If we now have 4 words or fewer as our title, and either no
            // 'hierarchical' separators (\, /, > or ») were found in the original
            // title or we decreased the number of words by more than 1 word, use
            // the original title.
            var curTitleWordCount = wordCount(curTitle);
            if (curTitleWordCount <= 4 &&
                (!titleHadHierarchicalSeparators ||
                 curTitleWordCount != wordCount(Patterns.TitleRegex5.Replace(origTitle, "")) - 1))
            {
                curTitle = origTitle;
            }

            return curTitle;
        }



        /// <summary>
        /// Prepare the HTML document for readability to scrape it.
        /// This includes things like stripping javascript, CSS, and handling terrible markup.
        /// </summary>
        void _prepDocument()
        {

            // Remove all style tags in head
            this._removeNodes(doc.Descendants(), p => p.Name == "style");
            var bodyElem = doc.SelectSingleNode("body");
            if (bodyElem != null)
            {
                this._replaceBrs(bodyElem);
            }


            this._replaceNodeTags(doc.SelectNodes("font"), "SPAN");


        }

        /**
         * Finds the next element, starting from the given node, and ignoring
         * whitespace in between. If the given node is an element, the same node is
         * returned.
         */
        HtmlNode _nextElement(HtmlNode node)
        {
            var next = node;
            while (next != null
                && (next.NodeType != HtmlNodeType.Element)
                && Patterns.Whitespace.Match(next.InnerText).Success)
            {
                next = next.NextSibling;
            }
            return next;
        }

        /**
         * Replaces 2 or more successive <br> elements with a single <p>.
         * Whitespace between <br> elements are ignored. For example:
         *   <div>foo<br>bar<br> <br><br>abc</div>
         * will become:
         *   <div>foo<br>bar<p>abc</p></div>
         */
        void _replaceBrs(HtmlNode elem)
        {
            foreach (var br in elem.SelectNodes("br"))
            {
                var next = br.NextSibling;

                // Whether 2 or more <br> elements have been found and replaced with a
                // <p> block.
                var replaced = false;

                // If we find a <br> chain, remove the <br>s until we hit another element
                // or non-whitespace. This leaves behind the first <br> in the chain
                // (which will be replaced with a <p> later).
                while (next != null && (next.Name == "BR"))
                {
                    replaced = true;
                    var brSibling = next.NextSibling;
                    next.ParentNode.RemoveChild(next);
                    next = brSibling;
                    next = this._nextElement(next);
                }

                // If we removed a <br> chain, replace the remaining <br> with a <p>. Add
                // all sibling nodes as children of the <p> until we hit another <br>
                // chain.
                if (replaced)
                {
                    var p = root.CreateElement("p");
                    br.ParentNode.ReplaceChild(p, br);

                    next = p.NextSibling;
                    while (next != null)
                    {
                        // If we've hit another <br><br>, we're done adding children to this <p>.
                        if (next.Name == "BR")
                        {
                            var nextElem = this._nextElement(next.NextSibling);
                            if (nextElem != null && nextElem.Name == "BR")
                                break;
                        }

                        // Otherwise, make this node a child of the new <p>.
                        var sibling = next.NextSibling;
                        p.AppendChild(next);
                        next = sibling;
                    }
                }
            }
        }


    }
}