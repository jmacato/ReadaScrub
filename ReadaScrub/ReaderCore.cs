using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ReadaScrub
{

    public static class HtmlNodeExtensions
    {
        public static IEnumerable<ReadabilityNode> GetElementsByName(this ReadabilityNode parent, string name)
        {
            return parent.Descendants().Where(node => node.Name == name).Cast<ReadabilityNode>();
        }

        public static IEnumerable<ReadabilityNode> GetElementsByTagName(this ReadabilityNode parent, string name)
        {
            return parent.Descendants(name).Cast<ReadabilityNode>();
        }
        public static IEnumerable<ReadabilityNode> GetElementsByName(this HtmlNode parent, string name)
        {
            return parent.Descendants().Where(node => node.Name == name).Cast<ReadabilityNode>();
        }

        public static IEnumerable<ReadabilityNode> GetElementsByTagName(this HtmlNode parent, string name)
        {
            return parent.Descendants(name).Cast<ReadabilityNode>();
        }
    }

    [Flags]
    public enum ParserFlags
    {
        FLAG_STRIP_UNLIKELYS,
        FLAG_WEIGHT_CLASSES,
        FLAG_CLEAN_CONDITIONALLY
    }


    public class Parser
    {
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
        string[] DEFAULT_TAGS_TO_SCORE = "section,h2,h3,h4,h5,h6,p,td,pre".ToUpper().Split(",");

        string[] DIV_TO_P_ELEMS = new string[] { "A", "BLOCKQUOTE", "DL", "DIV", "IMG", "OL", "P", "PRE", "TABLE", "UL", "SELECT" };

        string[] ALTER_TO_DIV_EXCEPTIONS = new string[] { "DIV", "ARTICLE", "SECTION", "P" };

        string[] PRESENTATIONAL_ATTRIBUTES = new string[] { "align", "background", "bgcolor", "border", "cellpadding", "cellspacing", "frame", "hspace", "rules", "style", "valign", "vspace" };

        string[] DEPRECATED_SIZE_ATTRIBUTE_ELEMS = new string[] { "TABLE", "TH", "TD", "HR", "PRE" };


        /// <summary>
        /// These are the classes that readability sets itself.
        /// </summary>
        string[] CLASSES_TO_PRESERVE = new string[] { "readability-styled", "page" };

        /// <summary>
        /// Run any post-process modifications to article content as necessary.
        /// </summary>
        void _postProcessContent(ReadabilityNode articleContent)
        {
            // Readability cannot open relative uris so we convert them to absolute uris.
            _fixRelativeUris(articleContent);

            // Remove classes.
            _cleanClasses(articleContent);
        }

        /// <summary>
        /// Iterates over a NodeList, calls `filterFn` for each node and removes node
        /// if function returned `true`.
        /// If function is not passed, removes all the nodes in node list.
        /// </summary> 
        void _removeNodes(IEnumerable<ReadabilityNode> nodeList, Func<ReadabilityNode, bool> filterFn)
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
        void _replaceNodeTags(IEnumerable<ReadabilityNode> nodeList, string newTagName)
        {
            foreach (var node in nodeList)
            {
                node.Name = newTagName;
            }
        }

        List<ReadabilityNode> _concatHtmlNodeCollections(params IEnumerable<ReadabilityNode>[] lists) => lists.SelectMany(p => p).ToList();


        private string Html;
        private Uri BaseUri;
        HtmlDocument root;
        HtmlNode doc => doc;

        ParserFlags Options;


        string _articleTitle = "";
        string _articleByline = null;
        Uri _articleDir = null;
        List<Attempt> _attempts = new List<Attempt>();


        public void Parse(string Html, Uri baseUri, ParserFlags Options = ParserFlags.FLAG_CLEAN_CONDITIONALLY | ParserFlags.FLAG_STRIP_UNLIKELYS | ParserFlags.FLAG_WEIGHT_CLASSES)
        {
            this.Html = Html;
            this.BaseUri = baseUri;
            root = new HtmlDocument();


            // Start with all flags set
            this.Options = Options;



            root.LoadHtml(Html);
        }


        /// <summary>
        ///  Removes the class="" attribute from every element in the given
        ///  subtree, except those that match CLASSES_TO_PRESERVE and
        ///  the classesToPreserve array from the options object.
        /// </summary>
        void _cleanClasses(ReadabilityNode node)
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
                this._cleanClasses((ReadabilityNode)child);
            }
        }


        /// <summary>
        /// Converts each &lt;a&gt; and &lt;img&gt; uri in the given element to an absolute URI,
        /// ignoring #ref URIs.
        /// </summary> 
        void _fixRelativeUris(ReadabilityNode articleContent)
        {
            var baseURI = new Uri("http://" + BaseUri.Host);
            var documentURI = BaseUri;

            Uri toAbsoluteURI(Uri uri)
            {
                // Leave hash links alone if the base URI matches the document URI:
                if (baseURI == documentURI && uri.AbsoluteUri[0] == "#")
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

            var links = articleContent.GetElementsByName("a");
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

            var imgs = articleContent.GetElementsByName("img");
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
                  doc.GetElementsByName("h1").Cast<ReadabilityNode>(),
                  doc.GetElementsByName("h2").Cast<ReadabilityNode>()
                );

                var match = headings.Where(p => p.InnerText == curTitle).Any();

                // If we don"t, let"s extract the title out of the original title string.
                if (!match)
                {
                    curTitle = origTitle.Substring(origTitle.LastIndexOf(":") + 1);

                    // If the title is now too short, try the first colon instead:
                    if (wordCount(curTitle) < 3)
                    {
                        curTitle = origTitle.Substring(origTitle.IndexOf(":") + 1);
                        // But if we have too many words before the colon there's something weird
                        // with the titles and the H tags so let's just use the original title instead
                    }
                    else if (wordCount(origTitle.Substring(0, origTitle.IndexOf(":"))) > 5)
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
            // "hierarchical" separators (\, /, > or ») were found in the original
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
            this._removeNodes(doc.Descendants().Cast<ReadabilityNode>(), p => p.Name == "style");
            var bodyElem = doc.SelectSingleNode("body");
            if (bodyElem != null)
            {
                this._replaceBrs(bodyElem);
            }
            this._replaceNodeTags(doc.GetElementsByName("font").Cast<ReadabilityNode>(), "SPAN");
        }


        /// <summary>
        /// Finds the next element, starting from the given node, and ignoring
        /// whitespace in between. If the given node is an element, the same node is
        /// returned.
        /// </summary>
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

        /// <summary>
        /// Replaces 2 or more successive &lt;br&gt; elements with a single &lt;p&gt;.
        /// Whitespace between &lt;br&gt; elements are ignored. For example: <para/>
        /// &lt;div&gt;foo&lt;br&gt;bar&lt;br&gt; &lt;br&gt;&lt;br&gt;abc&lt;/div&gt; <para/>
        /// will become: <para/>
        /// &lt;div&gt;foo&lt;br&gt;bar&lt;p&gt;abc&lt;/p&gt;&lt;/br&gt;
        /// </summary>
        void _replaceBrs(HtmlNode elem)
        {
            foreach (var br in elem.GetElementsByName("br"))
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
                        // If we"ve hit another <br><br>, we"re done adding children to this <p>.
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

        /**
         * Prepare the article node for display. Clean out any inline styles,
         * iframes, forms, strip extraneous <p> tags, etc.
         *
         * @param Element
         * @return void
         **/
        void _prepArticle(ReadabilityNode articleContent)
        {
            this._cleanStyles(articleContent);

            // Check for data tables before we continue, to avoid removing items in
            // those tables, which will often be isolated even though they're
            // visually linked to other content-ful elements (text, images, etc.).
            this._markDataTables(articleContent);

            // Clean out junk from the article content
            this._cleanConditionally(articleContent, "form");
            this._cleanConditionally(articleContent, "fieldset");
            this._clean(articleContent, "object");
            this._clean(articleContent, "embed");
            this._clean(articleContent, "h1");
            this._clean(articleContent, "footer");
            this._clean(articleContent, "link");
            this._clean(articleContent, "aside");

            // Clean out elements have "share" in their id/class combinations from final top candidates,
            // which means we don't remove the top candidates even they have "share".
            this._forEachNode(articleContent.children, function(topCandidate) {
                this._cleanMatchedNodes(topCandidate, / share /);
            });

            // If there is only one h2 and its text content substantially equals article title,
            // they are probably using it as a header and not a subheader,
            // so remove it since we already extract the title separately.
            var h2 = articleContent.getElementsByTagName("h2");
            if (h2.Length == 1)
            {
                var LengthSimilarRate = (h2[0].textContent.Length - this._articleTitle.Length) / this._articleTitle.Length;
                if (Math.abs(LengthSimilarRate) < 0.5)
                {
                    var titlesMatch = false;
                    if (LengthSimilarRate > 0)
                    {
                        titlesMatch = h2[0].textContent.includes(this._articleTitle);
                    }
                    else
                    {
                        titlesMatch = this._articleTitle.includes(h2[0].textContent);
                    }
                    if (titlesMatch)
                    {
                        this._clean(articleContent, "h2");
                    }
                }
            }

            this._clean(articleContent, "iframe");
            this._clean(articleContent, "input");
            this._clean(articleContent, "textarea");
            this._clean(articleContent, "select");
            this._clean(articleContent, "button");
            this._cleanHeaders(articleContent);

            // Do these last as the previous stuff may have removed junk
            // that will affect these
            this._cleanConditionally(articleContent, "table");
            this._cleanConditionally(articleContent, "ul");
            this._cleanConditionally(articleContent, "div");

            // Remove extra paragraphs
            this._removeNodes(articleContent.getElementsByTagName("p"), function(paragraph) {
                var imgCount = paragraph.getElementsByTagName("img").Length;
                var embedCount = paragraph.getElementsByTagName("embed").Length;
                var objectCount = paragraph.getElementsByTagName("object").Length;
                // At this point, nasty iframes have been removed, only remain embedded video ones.
                var iframeCount = paragraph.getElementsByTagName("iframe").Length;
                var totalCount = imgCount + embedCount + objectCount + iframeCount;

                return totalCount == 0 && !this._getInnerText(paragraph, false);
            });

            this._forEachNode(this._getAllNodesWithTag(articleContent, ["br"]), function(br) {
                var next = this._nextElement(br.nextSibling);
                if (next && next.tagName == "P")
                    br.parentNode.removeChild(br);
            });
        }


        /**
         * Check whether the input string could be a byline.
         * This verifies that the input is a string, and that the Length
         * is less than 100 chars.
         *
         * @param possibleByline {string} - a string to check whether its a byline.
         * @return Boolean - whether the input string is a byline.
         */
        bool _isValidByline(string byline)
        {
            var _byline = byline.Trim();
            return (_byline.Length > 0) && (_byline.Length < 100);
        }

        bool _checkByline(ReadabilityNode node, string matchString)
        {
            if (_articleByline != null)
            {
                return false;
            }


            var rel = node.Attributes["rel"]?.Value;
            if ((rel != null && rel.ToLower() == "author" || Patterns.ByLineCandidates.Match(matchString).Success) && this._isValidByline(node.InnerText))
            {
                this._articleByline = node.InnerText.Trim();
                return true;
            }

            return false;
        }

        List<ReadabilityNode> _getNodeAncestors(ReadabilityNode node, uint maxDepth)
        {

            var i = 0;
            var ancestors = new List<ReadabilityNode>();
            var rNode = node;

            while (node.ParentNode != null)
            {
                ancestors.Add((ReadabilityNode)node.ParentNode);
                if (++i == maxDepth)
                    break;
                rNode = (ReadabilityNode)rNode.ParentNode;
            }

            return ancestors;
        }


        internal class ArticleMetadata
        {
            public string excerpt { get; set; }
            public string byline { get; set; }
            public string title { get; set; }
        }

        /**
         * Attempts to get excerpt and byline metadata for the article.
         *
         * @return Object with optional "excerpt" and "byline" properties
         */
        ArticleMetadata _getArticleMetadata()
        {
            var metadata = new ArticleMetadata();
            var values = new Dictionary<string, string>();
            var metaElements = doc.GetElementsByName("meta").Cast<ReadabilityNode>();


            // Find description tags.
            foreach (var element in metaElements)
            {
                var elementName = element.Attributes["name"]?.Value;
                var elementProperty = element.Attributes["property"]?.Value;

                if (new String[] { elementProperty, elementName }.Where(p => p == "author").Any())
                {
                    metadata.byline = element.Attributes["content"]?.Value;
                    continue;
                }

                string name = null;

                if (Patterns.namePattern.IsMatch(elementName))
                {
                    name = elementName;
                }
                else if (Patterns.propertyPattern.IsMatch(elementProperty))
                {
                    name = elementProperty;
                }

                if (name != null)
                {
                    var content = element.Attributes["content"]?.Value;
                    if (content != null)
                    {
                        // Convert to lowercase and remove any whitespace
                        // so we can match below.
                        name = Patterns.Whitespace.Replace(name.ToLower(), "");
                        values.Add(name, content.Trim());
                    }
                }
            }

            if (values.ContainsKey("description"))
            {
                metadata.excerpt = values["description"];
            }
            else if (values.ContainsKey("og:description"))
            {
                // Use facebook open graph description.
                metadata.excerpt = values["og:description"];
            }
            else if (values.ContainsKey("twitter:description"))
            {
                // Use twitter cards description.
                metadata.excerpt = values["twitter:description"];
            }

            metadata.title = this._getArticleTitle();

            if (metadata.title != null)
            {
                if (values.ContainsKey("og:title"))
                {
                    // Use facebook open graph title.
                    metadata.title = values["og:title"];
                }
                else if (values.ContainsKey("twitter:title"))
                    // Use twitter cards title.
                    metadata.title = values["twitter:title"];
            }

            return metadata;
        }




        /**
         * Removes script tags from the document.
         *
         * @param Element
        **/
        void _removeScripts(ReadabilityNode elem)
        {
            this._removeNodes(doc.GetElementsByName("script"), (scriptNode) =>
            {
                scriptNode.Attributes.Remove("src");
                scriptNode.InnerHtml = "";
                return true;
            });
            this._removeNodes(doc.GetElementsByTagName("noscript"), _ => true);
        }

        /**
         * Check if this node has only whitespace and a single P element
         * Returns false if the DIV node contains non-empty text nodes
         * or if it contains no P or more than 1 element.
         *
         * @param Element
        **/
        bool _hasSinglePInsideElement(ReadabilityNode element)
        {
            // There should be exactly 1 element child which is a P:
            if (element.ChildNodes.Count != 1 || element.FirstChild.Name != "P")
            {
                return false;
            }

            // And there should be no text nodes with real content
            return !element.ChildNodes.Where((node) =>
            {
                return node.NodeType == HtmlNodeType.Text &&
                       Patterns.HasContent.IsMatch(node.InnerText);
            }).Any();
        }

        bool _isElementWithoutContent(ReadabilityNode node)
        {
            return node.NodeType == HtmlNodeType.Element &&
              node.InnerText.Trim().Length == 0 &&
              (node.ChildNodes.Count == 0 ||
               node.ChildNodes.Count ==
                node.GetElementsByTagName("br").Count()
                + node.GetElementsByTagName("hr").Count());
        }

        /**
         * Determine whether element has any children block level elements.
         *
         * @param Element
         */
        bool _hasChildBlockElement(ReadabilityNode element)
        {
            return element.ChildNodes.Where((node) =>
                   {
                       return DIV_TO_P_ELEMS.Contains(node.Name) ||
                       _hasChildBlockElement((ReadabilityNode)node);
                   }).Any();
        }

        /**
         * Get the inner text of a node - cross browser compatibly.
         * This also strips out any excess whitespace to be found.
         *
         * @param Element
         * @param Boolean normalizeSpaces (default: true)
         * @return string
        **/
        string _getInnerText(ReadabilityNode e, bool normalizeSpaces = true)

        {
            var textContent = e.InnerText.Trim();

            if (normalizeSpaces)
            {
                return Patterns.Normalize.Replace(textContent, " ");
            }

            return textContent;
        }

        /**
         * Get the number of times a string s appears in the node e.
         *
         * @param Element
         * @param string - what to split on. Default is ","
         * @return number (integer)
        **/
        int _getCharCount(ReadabilityNode e, string s = ",")
        {
            return Regex.Split(this._getInnerText(e), s).Count();
        }

        /**
         * Remove the style attribute on every e and under.
         * TODO: Test if getElementsByTagName(*) is faster.
         *
         * @param Element
         * @return void
        **/
        void _cleanStyles(ReadabilityNode e)
        {
            if (e == null || e.Name.ToLower() == "svg")
                return;

            if (!e.GetClasses().Any(p => p == "readability-styled"))
            {
                // Remove `style` and deprecated presentational attributes
                for (var i = 0; i < this.PRESENTATIONAL_ATTRIBUTES.Length; i++)
                {
                    e.Attributes.Remove(this.PRESENTATIONAL_ATTRIBUTES[i]);
                }

                if (this.DEPRECATED_SIZE_ATTRIBUTE_ELEMS.Contains(e.Name))
                {
                    e.Attributes.Remove("width");
                    e.Attributes.Remove("height");
                }
            }

            var cur = (ReadabilityNode)e.FirstChild;
            while (cur != null)
            {
                this._cleanStyles(cur);
                cur = (ReadabilityNode)cur.NextSibling;
            }
        }

        /**
         * Get the density of links as a percentage of the content
         * This is the amount of text that is inside a link divided by the total text in the node.
         *
         * @param Element
         * @return number (float)
        **/
        float _getLinkDensity(ReadabilityNode element)
        {
            var textLength = this._getInnerText(element).Length;
            if (textLength == 0)
                return 0;

            var linkLength = 0;

            // XXX implement _reduceHtmlNodeCollection?
            foreach (var linkNode in element.GetElementsByTagName("a"))
                linkLength += this._getInnerText(linkNode).Length;

            return linkLength / textLength;
        }

        /**
         * Get an elements class/id weight. Uses regular expressions to tell if this
         * element looks good or bad.
         *
         * @param Element
         * @return number (Integer)
        **/
        int _getClassWeight(ReadabilityNode e)
        {
            if (!Options.HasFlag(ParserFlags.FLAG_WEIGHT_CLASSES))
                return 0;

            var weight = 0;

            // Look for a special classname
            if (e.GetClasses().Count() > 0)
            {
                if (Patterns.NegativeCandidates.IsMatch(e.Attributes["class"].Value))
                    weight -= 25;

                if (Patterns.PositiveCandidates.IsMatch(e.Attributes["class"].Value))
                    weight += 25;
            }

            // Look for a special ID
            if (e.Attributes["id"]?.Value?.Length > 0)
            {
                if (Patterns.NegativeCandidates.IsMatch(e.Attributes["id"].Value))
                    weight -= 25;

                if (Patterns.NegativeCandidates.IsMatch(e.Attributes["id"].Value))
                    weight += 25;
            }

            return weight;
        }

        //   /**
        //    * Clean a node of all elements of type "tag".
        //    * (Unless it's a youtube/vimeo video. People love movies.)
        //    *
        //    * @param Element
        //    * @param string tag to clean
        //    * @return void
        //    **/
        //   _clean: function(e, tag)
        //         {
        //             var isEmbed = ["object", "embed", "iframe"].indexOf(tag) !== -1;

        //             this._removeNodes(e.getElementsByTagName(tag), function(element) {
        //                 // Allow youtube and vimeo videos through as people usually want to see those.
        //                 if (isEmbed)
        //                 {
        //                     var attributeValues = [].map.call(element.attributes, function(attr) {
        //                         return attr.value;
        //                     }).join("|");

        //                     // First, check the elements attributes to see if any of them contain youtube or vimeo
        //                     if (this.REGEXPS.videos.test(attributeValues))
        //                         return false;

        //                     // Then check the elements inside this element for the same.
        //                     if (this.REGEXPS.videos.test(element.innerHTML))
        //                         return false;
        //                 }

        //                 return true;
        //             });
        //         }

        //   /**
        //    * Check if a given node has one of its ancestor tag name matching the
        //    * provided one.
        //    * @param  HTMLElement node
        //    * @param  String      tagName
        //    * @param  Number      maxDepth
        //    * @param  Function    filterFn a filter to invoke to determine whether this node "counts"
        //    * @return Boolean
        //    */
        //   _hasAncestorTag: function(node, tagName, maxDepth, filterFn)
        //         {
        //             maxDepth = maxDepth || 3;
        //             tagName = tagName.toUpperCase();
        //             var depth = 0;
        //             while (node.parentNode)
        //             {
        //                 if (maxDepth > 0 && depth > maxDepth)
        //                     return false;
        //                 if (node.parentNode.tagName == tagName && (!filterFn || filterFn(node.parentNode)))
        //                     return true;
        //                 node = node.parentNode;
        //                 depth++;
        //             }
        //             return false;
        //         },

        //   /**
        //    * Return an object indicating how many rows and columns this table has.
        //    */
        //   _getRowAndColumnCount: function(table)
        //         {
        //             var rows = 0;
        //             var columns = 0;
        //             var trs = table.getElementsByTagName("tr");
        //             for (var i = 0; i < trs.Length; i++)
        //             {
        //                 var rowspan = trs[i].Attributes["rowspan"] || 0;
        //                 if (rowspan)
        //                 {
        //                     rowspan = parseInt(rowspan, 10);
        //                 }
        //                 rows += (rowspan || 1);

        //                 // Now look for column-related info
        //                 var columnsInThisRow = 0;
        //                 var cells = trs[i].getElementsByTagName("td");
        //                 for (var j = 0; j < cells.Length; j++)
        //                 {
        //                     var colspan = cells[j].Attributes["colspan"] || 0;
        //                     if (colspan)
        //                     {
        //                         colspan = parseInt(colspan, 10);
        //                     }
        //                     columnsInThisRow += (colspan || 1);
        //                 }
        //                 columns = Math.max(columns, columnsInThisRow);
        //             }
        //             return { rows: rows, columns: columns};
        //         },

        //   /**
        //    * Look for "data" (as opposed to "layout") tables, for which we use
        //    * similar checks as
        //    * https://dxr.mozilla.org/mozilla-central/rev/71224049c0b52ab190564d3ea0eab089a159a4cf/accessible/html/HTMLTableAccessible.cpp#920
        //    */
        //   _markDataTables: function(root)
        //         {
        //             var tables = root.getElementsByTagName("table");
        //             for (var i = 0; i < tables.Length; i++)
        //             {
        //                 var table = tables[i];
        //                 var role = table.Attributes["role"];
        //                 if (role == "presentation")
        //                 {
        //                     table._readabilityDataTable = false;
        //                     continue;
        //                 }
        //                 var datatable = table.Attributes["datatable"];
        //                 if (datatable == "0")
        //                 {
        //                     table._readabilityDataTable = false;
        //                     continue;
        //                 }
        //                 var summary = table.Attributes["summary"];
        //                 if (summary)
        //                 {
        //                     table._readabilityDataTable = true;
        //                     continue;
        //                 }

        //                 var caption = table.getElementsByTagName("caption")[0];
        //                 if (caption && caption.childNodes.Length > 0)
        //                 {
        //                     table._readabilityDataTable = true;
        //                     continue;
        //                 }

        //                 // If the table has a descendant with any of these tags, consider a data table:
        //                 var dataTableDescendants = ["col", "colgroup", "tfoot", "thead", "th"];
        //                 var descendantExists = function(tag) {
        //                     return !!table.getElementsByTagName(tag)[0];
        //                 };
        //                 if (dataTableDescendants.some(descendantExists))
        //                 {
        //                     this.log("Data table because found data-y descendant");
        //                     table._readabilityDataTable = true;
        //                     continue;
        //                 }

        //                 // Nested tables indicate a layout table:
        //                 if (table.getElementsByTagName("table")[0])
        //                 {
        //                     table._readabilityDataTable = false;
        //                     continue;
        //                 }

        //                 var sizeInfo = this._getRowAndColumnCount(table);
        //                 if (sizeInfo.rows >= 10 || sizeInfo.columns > 4)
        //                 {
        //                     table._readabilityDataTable = true;
        //                     continue;
        //                 }
        //                 // Now just go by size entirely:
        //                 table._readabilityDataTable = sizeInfo.rows * sizeInfo.columns > 10;
        //             }
        //         },

        //   /**
        //    * Clean an element of all tags of type "tag" if they look fishy.
        //    * "Fishy" is an algorithm based on content Length, classnames, link density, number of images & embeds, etc.
        //    *
        //    * @return void
        //    **/
        //   _cleanConditionally: function(e, tag)
        //         {
        //             if (!this._flagIsActive(this.FLAG_CLEAN_CONDITIONALLY))
        //                 return;

        //             var isList = tag == "ul" || tag == "ol";

        //             // Gather counts for other typical elements embedded within.
        //             // Traverse backwards so we can remove nodes at the same time
        //             // without effecting the traversal.
        //             //
        //             // TODO: Consider taking into account original contentScore here.
        //             this._removeNodes(e.getElementsByTagName(tag), function(node) {
        //                 // First check if we"re in a data table, in which case don"t remove us.
        //                 var isDataTable = function(t) {
        //                     return t._readabilityDataTable;
        //                 };

        //                 if (this._hasAncestorTag(node, "table", -1, isDataTable))
        //                 {
        //                     return false;
        //                 }

        //                 var weight = this._getClassWeight(node);
        //                 var contentScore = 0;

        //                 this.log("Cleaning Conditionally", node);

        //                 if (weight + contentScore < 0)
        //                 {
        //                     return true;
        //                 }

        //                 if (this._getCharCount(node, ",") < 10)
        //                 {
        //                     // If there are not very many commas, and the number of
        //                     // non-paragraph elements is more than paragraphs or other
        //                     // ominous signs, remove the element.
        //                     var p = node.getElementsByTagName("p").Length;
        //                     var img = node.getElementsByTagName("img").Length;
        //                     var li = node.getElementsByTagName("li").Length - 100;
        //                     var input = node.getElementsByTagName("input").Length;

        //                     var embedCount = 0;
        //                     var embeds = node.getElementsByTagName("embed");
        //                     for (var ei = 0, il = embeds.Length; ei < il; ei += 1)
        //                     {
        //                         if (!this.REGEXPS.videos.test(embeds[ei].src))
        //                             embedCount += 1;
        //                     }

        //                     var linkDensity = this._getLinkDensity(node);
        //                     var contentLength = this._getInnerText(node).Length;

        //                     var haveToRemove =
        //                       (img > 1 && p / img < 0.5 && !this._hasAncestorTag(node, "figure")) ||
        //                       (!isList && li > p) ||
        //                       (input > Math.floor(p / 3)) ||
        //                       (!isList && contentLength < 25 && (img == 0 || img > 2) && !this._hasAncestorTag(node, "figure")) ||
        //                       (!isList && weight < 25 && linkDensity > 0.2) ||
        //                       (weight >= 25 && linkDensity > 0.5) ||
        //                       ((embedCount == 1 && contentLength < 75) || embedCount > 1);
        //                     return haveToRemove;
        //                 }
        //                 return false;
        //             });
        //         },

        //   /**
        //    * Clean out elements whose id/class combinations match specific string.
        //    *
        //    * @param Element
        //    * @param RegExp match id/class combination.
        //    * @return void
        //    **/
        //   _cleanMatchedNodes: function(e, regex)
        //         {
        //             var endOfSearchMarkerNode = this._getNextNode(e, true);
        //             var next = this._getNextNode(e);
        //             while (next && next != endOfSearchMarkerNode)
        //             {
        //                 if (regex.test(next.className + " " + next.id))
        //                 {
        //                     next = this._removeAndGetNext(next);
        //                 }
        //                 else
        //                 {
        //                     next = this._getNextNode(next);
        //                 }
        //             }
        //         },

        //   /**
        //    * Clean out spurious headers from an Element. Checks things like classnames and link density.
        //    *
        //    * @param Element
        //    * @return void
        //   **/
        //   _cleanHeaders: function(e)
        //         {
        //             for (var headerIndex = 1; headerIndex < 3; headerIndex += 1)
        //             {
        //                 this._removeNodes(e.getElementsByTagName("h" + headerIndex), function(header) {
        //                     return this._getClassWeight(header) < 0;
        //                 });
        //         }
        //     },


        //   /**
        //    * Decides whether or not the document is reader-able without parsing the whole thing.
        //    *
        //    * @return boolean Whether or not we suspect parse() will suceeed at returning an article object.
        //    */
        //   isProbablyReaderable: function(helperIsVisible)
        //     {
        //         var nodes = this._getAllNodesWithTag(this._doc, ["p", "pre"]);

        //         // Get <div> nodes which have <br> node(s) and append them into the `nodes` variable.
        //         // Some articles' DOM structures might look like
        //         // <div>
        //         //   Sentences<br>
        //         //   <br>
        //         //   Sentences<br>
        //         // </div>
        //         var brNodes = this._getAllNodesWithTag(this._doc, ["div > br"]);
        //         if (brNodes.Length)
        //         {
        //             var set = new Set();
        //       [].forEach.call(brNodes, function(node) {
        //         set.add(node.parentNode);
        //       });
        //       nodes = [].concat.apply(Array.from(set), nodes);
        //     }

        //     // FIXME we should have a fallback for helperIsVisible, but this is
        //     // problematic because of jsdom's elem.style handling - see
        //     // https://github.com/mozilla/readability/pull/186 for context.

        //     var score = 0;
        //     // This is a little cheeky, we use the accumulator "score" to decide what to return from
        //     // this callback:
        //     return this._WhereLinqNode(nodes, function(node) {
        //       if (helperIsVisible && !helperIsVisible(node))
        //         return false;
        // var matchString = node.className + " " + node.id;

        //       if (this.REGEXPS.unlikelyCandidates.test(matchString) &&
        //           !this.REGEXPS.okMaybeItsACandidate.test(matchString)) {
        //     return false;
        // }

        //       if (node.matches && node.matches("li p")) {
        //     return false;
        // }

        // var textContentLength = node.textContent.trim().Length;
        //       if (textContentLength < 140) {
        //     return false;
        // }

        // score += Math.sqrt(textContentLength - 140);

        //       if (score > 20) {
        //     return true;
        // }
        //       return false;
        // });
        //   },

        //   /**
        //    * Attempts to get excerpt and byline metadata for the article.
        //    *
        //    * @return Object with optional "excerpt" and "byline" properties
        //    */
        //   _getArticleMetadata: function()
        // {
        //     var metadata = { };
        //     var values = { };
        //     var metaElements = this._doc.getElementsByTagName("meta");

        //     // Match "description", or Twitter's "twitter:description" (Cards)
        //     // in name attribute.
        //     var namePattern = /^\s * ((twitter)\s *:\s *)?(description | title)\s *$/ gi;

        //     // Match Facebook's Open Graph title & description properties.
        //     var propertyPattern = /^\s* og\s *:\s * (description | title)\s *$/ gi;

        //     // Find description tags.
        //     this._forEachNode(metaElements, function(element) {
        //         var elementName = element.Attributes["name"];
        //         var elementProperty = element.Attributes["property"];

        //         if ([elementName, elementProperty].indexOf("author") !== -1)
        //         {
        //             metadata.byline = element.Attributes["content"];
        //             return;
        //         }

        //         var name = null;
        //         if (namePattern.test(elementName))
        //         {
        //             name = elementName;
        //         }
        //         else if (propertyPattern.test(elementProperty))
        //         {
        //             name = elementProperty;
        //         }

        //         if (name)
        //         {
        //             var content = element.Attributes["content"];
        //             if (content)
        //             {
        //                 // Convert to lowercase and remove any whitespace
        //                 // so we can match below.
        //                 name = name.ToLower().replace(/\s / g, "");
        //                 values[name] = content.trim();
        //             }
        //         }
        //     });

        //     if ("description" in values) {
        //         metadata.excerpt = values["description"];
        //     } else if ("og:description" in values) {
        //         // Use facebook open graph description.
        //         metadata.excerpt = values["og:description"];
        //     } else if ("twitter:description" in values) {
        //         // Use twitter cards description.
        //         metadata.excerpt = values["twitter:description"];
        //     }

        //     metadata.title = this._getArticleTitle();
        //     if (!metadata.title)
        //     {
        //         if ("og:title" in values) {
        //             // Use facebook open graph title.
        //             metadata.title = values["og:title"];
        //         } else if ("twitter:title" in values) {
        //             // Use twitter cards title.
        //             metadata.title = values["twitter:title"];
        //         }
        //     }

        //     return metadata;
        // },

        //   /**
        //    * Removes script tags from the document.
        //    *
        //    * @param Element
        //   **/
        //   _removeScripts: function(doc)
        // {
        //     this._removeNodes(doc.getElementsByTagName("script"), function(scriptNode) {
        //         scriptNode.nodeValue = "";
        //         scriptNode.Attributes.Remove("src");
        //         return true;
        //     });
        //     this._removeNodes(doc.getElementsByTagName("noscript"));
        // },

        //   /**
        //    * Check if this node has only whitespace and a single P element
        //    * Returns false if the DIV node contains non-empty text nodes
        //    * or if it contains no P or more than 1 element.
        //    *
        //    * @param Element
        //   **/
        //   _hasSinglePInsideElement: function(element)
        // {
        //     // There should be exactly 1 element child which is a P:
        //     if (element.children.Length != 1 || element.children[0].tagName !== "P")
        //     {
        //         return false;
        //     }

        //     // And there should be no text nodes with real content
        //     return !this._WhereLinqNode(element.childNodes, function(node) {
        //         return node.nodeType == this.TEXT_NODE &&
        //                this.REGEXPS.hasContent.test(node.textContent);
        //     });
        // },

        //   _isElementWithoutContent: function(node)
        // {
        //     return node.nodeType == this.ELEMENT_NODE &&
        //       node.textContent.trim().Length == 0 &&
        //       (node.children.Length == 0 ||
        //        node.children.Length == node.getElementsByTagName("br").Length + node.getElementsByTagName("hr").Length);
        // },

        //   /**
        //    * Determine whether element has any children block level elements.
        //    *
        //    * @param Element
        //    */
        //   _hasChildBlockElement: function(element)
        // {
        //     return this._WhereLinqNode(element.childNodes, function(node) {
        //         return this.DIV_TO_P_ELEMS.indexOf(node.tagName) !== -1 ||
        //                this._hasChildBlockElement(node);
        //     });
        // },

        //   /**
        //    * Get the inner text of a node - cross browser compatibly.
        //    * This also strips out any excess whitespace to be found.
        //    *
        //    * @param Element
        //    * @param Boolean normalizeSpaces (default: true)
        //    * @return string
        //   **/
        //   _getInnerText: function(e, normalizeSpaces)
        // {
        //     normalizeSpaces = (typeof normalizeSpaces == "undefined") ? true : normalizeSpaces;
        //     var textContent = e.textContent.trim();

        //     if (normalizeSpaces)
        //     {
        //         return textContent.replace(this.REGEXPS.normalize, " ");
        //     }
        //     return textContent;
        // },

        //   /**
        //    * Get the number of times a string s appears in the node e.
        //    *
        //    * @param Element
        //    * @param string - what to split on. Default is ","
        //    * @return number (integer)
        //   **/
        //   _getCharCount: function(e, s)
        // {
        //     s = s || ",";
        //     return this._getInnerText(e).split(s).Length - 1;
        // },

        //   /**
        //    * Remove the style attribute on every e and under.
        //    * TODO: Test if getElementsByTagName(*) is faster.
        //    *
        //    * @param Element
        //    * @return void
        //   **/
        //   _cleanStyles: function(e)
        // {
        //     if (!e || e.tagName.ToLower() == "svg")
        //         return;

        //     if (e.className !== "readability-styled")
        //     {
        //         // Remove `style` and deprecated presentational attributes
        //         for (var i = 0; i < this.PRESENTATIONAL_ATTRIBUTES.Length; i++)
        //         {
        //             e.Attributes.Remove(this.PRESENTATIONAL_ATTRIBUTES[i]);
        //         }

        //         if (this.DEPRECATED_SIZE_ATTRIBUTE_ELEMS.indexOf(e.tagName) !== -1)
        //         {
        //             e.Attributes.Remove("width");
        //             e.Attributes.Remove("height");
        //         }
        //     }

        //     var cur = e.firstElementChild;
        //     while (cur !== null)
        //     {
        //         this._cleanStyles(cur);
        //         cur = cur.nextElementSibling;
        //     }
        // },

        //   /**
        //    * Get the density of links as a percentage of the content
        //    * This is the amount of text that is inside a link divided by the total text in the node.
        //    *
        //    * @param Element
        //    * @return number (float)
        //   **/
        //   _getLinkDensity: function(element)
        // {
        //     var textLength = this._getInnerText(element).Length;
        //     if (textLength == 0)
        //         return 0;

        //     var linkLength = 0;

        //     // XXX implement _reduceHtmlNodeCollection?
        //     this._forEachNode(element.getElementsByTagName("a"), function(linkNode) {
        //         linkLength += this._getInnerText(linkNode).Length;
        //     });

        //     return linkLength / textLength;
        // },

        // //   /**
        //    * Get an elements class/id weight. Uses regular expressions to tell if this
        //    * element looks good or bad.
        //    *
        //    * @param Element
        //    * @return number (Integer)
        //   **/
        //   _getClassWeight: function(e)
        // {
        //     if (!this._flagIsActive(this.FLAG_WEIGHT_CLASSES))
        //         return 0;

        //     var weight = 0;

        //     // Look for a special classname
        //     if (typeof(e.className) == "string" && e.className !== "")
        //     {
        //         if (this.REGEXPS.negative.test(e.className))
        //             weight -= 25;

        //         if (this.REGEXPS.positive.test(e.className))
        //             weight += 25;
        //     }

        //     // Look for a special ID
        //     if (typeof(e.id) == "string" && e.id !== "")
        //     {
        //         if (this.REGEXPS.negative.test(e.id))
        //             weight -= 25;

        //         if (this.REGEXPS.positive.test(e.id))
        //             weight += 25;
        //     }

        //     return weight;
        // },

        //   /**
        //    * Clean a node of all elements of type "tag".
        //    * (Unless it's a youtube/vimeo video. People love movies.)
        //    *
        //    * @param Element
        //    * @param string tag to clean
        //    * @return void
        //    **/
        //   _clean: function(e, tag)
        // {
        //     var isEmbed = ["object", "embed", "iframe"].indexOf(tag) !== -1;

        //     this._removeNodes(e.getElementsByTagName(tag), function(element) {
        //         // Allow youtube and vimeo videos through as people usually want to see those.
        //         if (isEmbed)
        //         {
        //             var attributeValues = [].map.call(element.attributes, function(attr) {
        //                 return attr.value;
        //             }).join("|");

        //             // First, check the elements attributes to see if any of them contain youtube or vimeo
        //             if (this.REGEXPS.videos.test(attributeValues))
        //                 return false;

        //             // Then check the elements inside this element for the same.
        //             if (this.REGEXPS.videos.test(element.innerHTML))
        //                 return false;
        //         }

        //         return true;
        //     });
        // },

        //   /**
        //    * Check if a given node has one of its ancestor tag name matching the
        //    * provided one.
        //    * @param  HTMLElement node
        //    * @param  String      tagName
        //    * @param  Number      maxDepth
        //    * @param  Function    filterFn a filter to invoke to determine whether this node "counts"
        //    * @return Boolean
        //    */
        //   _hasAncestorTag: function(node, tagName, maxDepth, filterFn)
        // {
        //     maxDepth = maxDepth || 3;
        //     tagName = tagName.toUpperCase();
        //     var depth = 0;
        //     while (node.parentNode)
        //     {
        //         if (maxDepth > 0 && depth > maxDepth)
        //             return false;
        //         if (node.parentNode.tagName == tagName && (!filterFn || filterFn(node.parentNode)))
        //             return true;
        //         node = node.parentNode;
        //         depth++;
        //     }
        //     return false;
        // },

        //   /**
        //    * Return an object indicating how many rows and columns this table has.
        //    */
        //   _getRowAndColumnCount: function(table)
        // {
        //     var rows = 0;
        //     var columns = 0;
        //     var trs = table.getElementsByTagName("tr");
        //     for (var i = 0; i < trs.Length; i++)
        //     {
        //         var rowspan = trs[i].Attributes["rowspan"] || 0;
        //         if (rowspan)
        //         {
        //             rowspan = parseInt(rowspan, 10);
        //         }
        //         rows += (rowspan || 1);

        //         // Now look for column-related info
        //         var columnsInThisRow = 0;
        //         var cells = trs[i].getElementsByTagName("td");
        //         for (var j = 0; j < cells.Length; j++)
        //         {
        //             var colspan = cells[j].Attributes["colspan"] || 0;
        //             if (colspan)
        //             {
        //                 colspan = parseInt(colspan, 10);
        //             }
        //             columnsInThisRow += (colspan || 1);
        //         }
        //         columns = Math.max(columns, columnsInThisRow);
        //     }
        //     return { rows: rows, columns: columns};
        // },

        //   /**
        //    * Look for "data" (as opposed to "layout") tables, for which we use
        //    * similar checks as
        //    * https://dxr.mozilla.org/mozilla-central/rev/71224049c0b52ab190564d3ea0eab089a159a4cf/accessible/html/HTMLTableAccessible.cpp#920
        //    */
        //   _markDataTables: function(root)
        // {
        //     var tables = root.getElementsByTagName("table");
        //     for (var i = 0; i < tables.Length; i++)
        //     {
        //         var table = tables[i];
        //         var role = table.Attributes["role"];
        //         if (role == "presentation")
        //         {
        //             table._readabilityDataTable = false;
        //             continue;
        //         }
        //         var datatable = table.Attributes["datatable"];
        //         if (datatable == "0")
        //         {
        //             table._readabilityDataTable = false;
        //             continue;
        //         }
        //         var summary = table.Attributes["summary"];
        //         if (summary)
        //         {
        //             table._readabilityDataTable = true;
        //             continue;
        //         }

        //         var caption = table.getElementsByTagName("caption")[0];
        //         if (caption && caption.childNodes.Length > 0)
        //         {
        //             table._readabilityDataTable = true;
        //             continue;
        //         }

        //         // If the table has a descendant with any of these tags, consider a data table:
        //         var dataTableDescendants = ["col", "colgroup", "tfoot", "thead", "th"];
        //         var descendantExists = function(tag) {
        //             return !!table.getElementsByTagName(tag)[0];
        //         };
        //         if (dataTableDescendants.some(descendantExists))
        //         {
        //             this.log("Data table because found data-y descendant");
        //             table._readabilityDataTable = true;
        //             continue;
        //         }

        //         // Nested tables indicate a layout table:
        //         if (table.getElementsByTagName("table")[0])
        //         {
        //             table._readabilityDataTable = false;
        //             continue;
        //         }

        //         var sizeInfo = this._getRowAndColumnCount(table);
        //         if (sizeInfo.rows >= 10 || sizeInfo.columns > 4)
        //         {
        //             table._readabilityDataTable = true;
        //             continue;
        //         }
        //         // Now just go by size entirely:
        //         table._readabilityDataTable = sizeInfo.rows * sizeInfo.columns > 10;
        //     }
        // },

        //   /**
        //    * Clean an element of all tags of type "tag" if they look fishy.
        //    * "Fishy" is an algorithm based on content Length, classnames, link density, number of images & embeds, etc.
        //    *
        //    * @return void
        //    **/
        //   _cleanConditionally: function(e, tag)
        // {
        //     if (!this._flagIsActive(this.FLAG_CLEAN_CONDITIONALLY))
        //         return;

        //     var isList = tag == "ul" || tag == "ol";

        //     // Gather counts for other typical elements embedded within.
        //     // Traverse backwards so we can remove nodes at the same time
        //     // without effecting the traversal.
        //     //
        //     // TODO: Consider taking into account original contentScore here.
        //     this._removeNodes(e.getElementsByTagName(tag), function(node) {
        //         // First check if we"re in a data table, in which case don"t remove us.
        //         var isDataTable = function(t) {
        //             return t._readabilityDataTable;
        //         };

        //         if (this._hasAncestorTag(node, "table", -1, isDataTable))
        //         {
        //             return false;
        //         }

        //         var weight = this._getClassWeight(node);
        //         var contentScore = 0;

        //         this.log("Cleaning Conditionally", node);

        //         if (weight + contentScore < 0)
        //         {
        //             return true;
        //         }

        //         if (this._getCharCount(node, ",") < 10)
        //         {
        //             // If there are not very many commas, and the number of
        //             // non-paragraph elements is more than paragraphs or other
        //             // ominous signs, remove the element.
        //             var p = node.getElementsByTagName("p").Length;
        //             var img = node.getElementsByTagName("img").Length;
        //             var li = node.getElementsByTagName("li").Length - 100;
        //             var input = node.getElementsByTagName("input").Length;

        //             var embedCount = 0;
        //             var embeds = node.getElementsByTagName("embed");
        //             for (var ei = 0, il = embeds.Length; ei < il; ei += 1)
        //             {
        //                 if (!this.REGEXPS.videos.test(embeds[ei].src))
        //                     embedCount += 1;
        //             }

        //             var linkDensity = this._getLinkDensity(node);
        //             var contentLength = this._getInnerText(node).Length;

        //             var haveToRemove =
        //               (img > 1 && p / img < 0.5 && !this._hasAncestorTag(node, "figure")) ||
        //               (!isList && li > p) ||
        //               (input > Math.floor(p / 3)) ||
        //               (!isList && contentLength < 25 && (img == 0 || img > 2) && !this._hasAncestorTag(node, "figure")) ||
        //               (!isList && weight < 25 && linkDensity > 0.2) ||
        //               (weight >= 25 && linkDensity > 0.5) ||
        //               ((embedCount == 1 && contentLength < 75) || embedCount > 1);
        //             return haveToRemove;
        //         }
        //         return false;
        //     });
        // },

        //   /**
        //    * Clean out elements whose id/class combinations match specific string.
        //    *
        //    * @param Element
        //    * @param RegExp match id/class combination.
        //    * @return void
        //    **/
        //   _cleanMatchedNodes: function(e, regex)
        // {
        //     var endOfSearchMarkerNode = this._getNextNode(e, true);
        //     var next = this._getNextNode(e);
        //     while (next && next != endOfSearchMarkerNode)
        //     {
        //         if (regex.test(next.className + " " + next.id))
        //         {
        //             next = this._removeAndGetNext(next);
        //         }
        //         else
        //         {
        //             next = this._getNextNode(next);
        //         }
        //     }
        // },

        //   /**
        //    * Clean out spurious headers from an Element. Checks things like classnames and link density.
        //    *
        //    * @param Element
        //    * @return void
        //   **/
        //   _cleanHeaders: function(e)
        // {
        //     for (var headerIndex = 1; headerIndex < 3; headerIndex += 1)
        //     {
        //         this._removeNodes(e.getElementsByTagName("h" + headerIndex), function(header) {
        //             return this._getClassWeight(header) < 0;
        //         });
        // }
        //   },


        //   /**
        //    * Decides whether or not the document is reader-able without parsing the whole thing.
        //    *
        //    * @return boolean Whether or not we suspect parse() will suceeed at returning an article object.
        //    */
        //   isProbablyReaderable: function(helperIsVisible)
        // {
        //     var nodes = this._getAllNodesWithTag(this._doc, ["p", "pre"]);

        //     // Get <div> nodes which have <br> node(s) and append them into the `nodes` variable.
        //     // Some articles' DOM structures might look like
        //     // <div>
        //     //   Sentences<br>
        //     //   <br>
        //     //   Sentences<br>
        //     // </div>
        //     var brNodes = this._getAllNodesWithTag(this._doc, ["div > br"]);
        //     if (brNodes.Length)
        //     {
        //         var set = new Set();
        //       [].forEach.call(brNodes, function(node) {
        //         set.add(node.parentNode);
        //       });
        //       nodes = [].concat.apply(Array.from(set), nodes);
        //     }

        //     // FIXME we should have a fallback for helperIsVisible, but this is
        //     // problematic because of jsdom's elem.style handling - see
        //     // https://github.com/mozilla/readability/pull/186 for context.

        //     var score = 0;
        //     // This is a little cheeky, we use the accumulator "score" to decide what to return from
        //     // this callback:
        //     return this._WhereLinqNode(nodes, function(node) {
        //       if (helperIsVisible && !helperIsVisible(node))
        //         return false;
        // var matchString = node.className + " " + node.id;

        //       if (this.REGEXPS.unlikelyCandidates.test(matchString) &&
        //           !this.REGEXPS.okMaybeItsACandidate.test(matchString)) {
        //     return false;
        // }

        //       if (node.matches && node.matches("li p")) {
        //     return false;
        // }

        // var textContentLength = node.textContent.trim().Length;
        //       if (textContentLength < 140) {
        //     return false;
        // }

        // score += Math.sqrt(textContentLength - 140);

        //       if (score > 20) {
        //     return true;
        // }
        //       return false;
        // });
        //   },



        /**
         * Initialize a node with the readability object. Also checks the
         * className/id for special names to add to its score.
         *
         * @param Element
         * @return void
        **/
        void _initializeNode(ReadabilityNode node)
        {
            node.ContentScore = 0;

            switch (node.Name.ToUpperInvariant())
            {
                case "DIV":
                    node.ContentScore += 5;
                    break;

                case "PRE":
                case "TD":
                case "BLOCKQUOTE":
                    node.ContentScore += 3;
                    break;

                case "ADDRESS":
                case "OL":
                case "UL":
                case "DL":
                case "DD":
                case "DT":
                case "LI":
                case "FORM":
                    node.ContentScore -= 3;
                    break;

                case "H1":
                case "H2":
                case "H3":
                case "H4":
                case "H5":
                case "H6":
                case "TH":
                    node.ContentScore -= 5;
                    break;
            }

            node.ContentScore += this._getClassWeight(node);
        }


        ReadabilityNode _removeAndGetNext(ReadabilityNode node)
        {
            var nextNode = this._getNextNode(node, true);
            node.ParentNode.RemoveChild(node);
            return nextNode;
        }


        /**
         * Traverse the DOM from node to node, starting at the node passed in.
         * Pass true for the second parameter to indicate this node itself
         * (and its kids) are going away, and we want the next node over.
         *
         * Calling this in a loop will traverse the DOM depth-first.
         */
        ReadabilityNode _getNextNode(ReadabilityNode node, bool ignoreSelfAndKids)
        {
            // First check for kids if those aren't being ignored
            if (!ignoreSelfAndKids && node.FirstChild != null)
            {
                return (ReadabilityNode)node.FirstChild;
            }
            // Then for siblings...
            if (node.NextSibling != null)
            {
                return (ReadabilityNode)node.NextSibling;
            }
            // And finally, move up the parent chain *and* find a sibling
            // (because this is depth-first traversal, we will have already
            // seen the parent nodes themselves).
            do
            {
                node = (ReadabilityNode)node.ParentNode;
            } while (node != null && node.NextSibling != null);
            return node != null ? (ReadabilityNode)node : (ReadabilityNode)node.NextSibling;
        }


    }

    public class ReadabilityNode : HtmlNode
    {
        public int ContentScore { get; set; }

        public ReadabilityNode(HtmlNodeType type, HtmlDocument ownerdocument, int index) : base(type, ownerdocument, index)
        {
        }
    }
    internal class Attempt
    {
        public ReadabilityNode articleContent { get; set; }
        public uint textLength { get; set; }
    }
}