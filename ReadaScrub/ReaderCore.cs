using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ReadaScrub
{

    public static class HtmlNodeExtensions
    {
        public static IEnumerable<HtmlNode> GetElementsByName(this HtmlNode parent, string name)
        {
            return parent.Descendants().Where(node => node.Name == name).Cast<HtmlNode>();
        }

        public static IEnumerable<HtmlNode> GetElementsByTagName(this HtmlNode parent, string name)
        {
            return parent.Descendants(name).Cast<HtmlNode>();
        }

        public static T GetRDProperty<T>(this HtmlNode node, string propName, string defaultVal = "null")
        {
            return Convert<T>(node.GetAttributeValue($"READABILITY_{propName}", defaultVal));
        }

        public static void SetRDProperty<T>(this HtmlNode node, string propName, T value)
        {
            node.SetAttributeValue($"READABILITY_{propName}", value.ToString());
        }


        public static T Convert<T>(string value)
        {
            try
            {
                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter != null)
                {
                    // Cast ConvertFromString(string text) : object to (T)
                    return (T)converter.ConvertFromString(value);
                }
                return default(T);
            }
            catch (NotSupportedException)
            {
                return default(T);
            }
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
        string[] DEFAULT_TAGS_TO_SCORE = "section,h2,h3,h4,h5,h6,p,td,pre".ToUpper().Split(',');

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
        void _postProcessContent(HtmlNode articleContent)
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
        void _removeNodes(IEnumerable<HtmlNode> nodeList, Func<HtmlNode, bool> filterFn)
        {
            foreach (var node in nodeList)
            {
                if (filterFn(node))
                {
                    node.ParentNode.RemoveChild(node);
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
        void _replaceNodeTags(IEnumerable<HtmlNode> nodeList, string newTagName)
        {
            foreach (var node in nodeList)
            {
                node.Name = newTagName;
            }
        }

        List<HtmlNode> _concatHtmlNodeCollections(params IEnumerable<HtmlNode>[] lists) => lists.SelectMany(p => p).ToList();


        private string Html;
        private Uri BaseUri;
        HtmlDocument root;
        HtmlNode doc => doc;

        ParserFlags Options;


        string _articleTitle = "";
        string _articleByline = null;
        Uri _articleDir = null;
        List<Attempt> _attempts = new List<Attempt>();


        public Parser(string Html, Uri baseUri, ParserFlags Options = ParserFlags.FLAG_CLEAN_CONDITIONALLY | ParserFlags.FLAG_STRIP_UNLIKELYS | ParserFlags.FLAG_WEIGHT_CLASSES)
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
                this._cleanClasses((HtmlNode)child);
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
                  doc.GetElementsByName("h1").Cast<HtmlNode>(),
                  doc.GetElementsByName("h2").Cast<HtmlNode>()
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
            this._removeNodes(doc.Descendants().Cast<HtmlNode>(), p => p.Name == "style");
            var bodyElem = doc.SelectSingleNode("body");
            if (bodyElem != null)
            {
                this._replaceBrs(bodyElem);
            }
            this._replaceNodeTags(doc.GetElementsByName("font").Cast<HtmlNode>(), "SPAN");
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
        void _prepArticle(HtmlNode articleContent)
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
            foreach (var topCandidate in articleContent.ChildNodes)
            {
                this._cleanMatchedNodes((HtmlNode)topCandidate, Patterns.prepArticle1);
            }

            // If there is only one h2 and its text content substantially equals article title,
            // they are probably using it as a header and not a subheader,
            // so remove it since we already extract the title separately.
            var h2 = articleContent.GetElementsByTagName("h2");
            if (h2.Count() == 1)
            {
                var h2i = h2.First().InnerText;
                var LengthSimilarRate = (h2i.Length - this._articleTitle.Length) / this._articleTitle.Length;
                if (Math.Abs(LengthSimilarRate) < 0.5)
                {
                    var titlesMatch = false;
                    if (LengthSimilarRate > 0)
                    {
                        titlesMatch = h2i.Contains(this._articleTitle);
                    }
                    else
                    {
                        titlesMatch = this._articleTitle.Contains(h2i);
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
            this._removeNodes(articleContent.GetElementsByTagName("p"), (paragraph) =>
            {
                var imgCount = paragraph.GetElementsByTagName("img").Count();
                var embedCount = paragraph.GetElementsByTagName("embed").Count();
                var objectCount = paragraph.GetElementsByTagName("object").Count();
                // At this point, nasty iframes have been removed, only remain embedded video ones.
                var iframeCount = paragraph.GetElementsByTagName("iframe").Count();
                var totalCount = imgCount + embedCount + objectCount + iframeCount;

                return totalCount == 0 && !(this._getInnerText(paragraph, false).Length > 0);
            });

            foreach (var br in this._getAllNodesWithTag(articleContent, "br"))
            {
                var next = this._nextElement(br.NextSibling);
                if (next != null && next.Name.ToUpper() == "P")
                    br.ParentNode.RemoveChild(br);
            }
        }


        List<HtmlNode> _getAllNodesWithTag(HtmlNode node, params string[] tagNames)
        {
            return node.Descendants()
                       .Select(p =>
                       {
                           var accumulator = new List<HtmlNode>();
                           foreach (var sel in tagNames)
                           {
                               var acc = new List<HtmlNode>();

                               foreach (var n in p.SelectNodes(sel))
                               {
                                   acc.Add((HtmlNode)n);
                               }
                           }
                           return accumulator;
                       })
                       .SelectMany(p => p)
                       .ToList();
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

        bool _checkByline(HtmlNode node, string matchString)
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

        List<HtmlNode> _getNodeAncestors(HtmlNode node, uint maxDepth)
        {

            var i = 0;
            var ancestors = new List<HtmlNode>();
            var rNode = (HtmlNode)node;

            while (node.ParentNode != null)
            {
                rNode.SetAttributeValue("READABILITY_Level", i.ToString());
                ancestors.Add((HtmlNode)rNode.ParentNode);
                if (++i == maxDepth)
                    break;
                rNode = (HtmlNode)rNode.ParentNode;
            }

            return ancestors;
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
            var metaElements = doc.GetElementsByName("meta").Cast<HtmlNode>();


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
        void _removeScripts(HtmlNode elem)
        {
            foreach (var k in doc.GetElementsByTagName("script"))
                k.ParentNode?.RemoveChild(k);
            foreach (var k in doc.GetElementsByTagName("noscript"))
                k.ParentNode?.RemoveChild(k);
        }

        /**
         * Check if this node has only whitespace and a single P element
         * Returns false if the DIV node contains non-empty text nodes
         * or if it contains no P or more than 1 element.
         *
         * @param Element
        **/
        bool _hasSinglePInsideElement(HtmlNode element)
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

        bool _isElementWithoutContent(HtmlNode node)
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
        bool _hasChildBlockElement(HtmlNode element)
        {
            return element.ChildNodes.Where((node) =>
                   {
                       return DIV_TO_P_ELEMS.Contains(node.Name) ||
                       _hasChildBlockElement((HtmlNode)node);
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
        string _getInnerText(HtmlNode e, bool normalizeSpaces = true)

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
        int _getCharCount(HtmlNode e, string s = ",")
        {
            return Regex.Split(this._getInnerText(e), s).Count();
        }

        /**
         * Remove the style attribute on every e and under.
         * TODO: Test if GetElementsByTagName(*) is faster.
         *
         * @param Element
         * @return void
        **/
        void _cleanStyles(HtmlNode e)
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

            var cur = (HtmlNode)e.FirstChild;
            while (cur != null)
            {
                this._cleanStyles(cur);
                cur = (HtmlNode)cur.NextSibling;
            }
        }

        /**
         * Get the density of links as a percentage of the content
         * This is the amount of text that is inside a link divided by the total text in the node.
         *
         * @param Element
         * @return number (float)
        **/
        float _getLinkDensity(HtmlNode element)
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
        int _getClassWeight(HtmlNode e)
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

        /**
         * Clean a node of all elements of type "tag".
         * (Unless it's a youtube/vimeo video. People love movies.)
         *
         * @param Element
         * @param string tag to clean
         * @return void
         **/
        void _clean(HtmlNode e, string tag)
        {
            var embedTags = new string[] { "object", "embed", "iframe" };

            var isEmbed = embedTags.Contains(tag);

            this._removeNodes(e.GetElementsByTagName(tag), (element) =>
            {
                // Allow youtube and vimeo videos through as people usually want to see those.
                if (isEmbed)
                {
                    var attributeValues = String
                        .Join("|",
                            e.Attributes
                                .Where(p => p?.Value.Length > 0)
                                .SelectMany(p => p?.Value));

                    // First, check the elements attributes to see if any of them contain youtube or vimeo
                    if (Patterns.Videos.IsMatch(attributeValues))
                        return false;

                    // Then check the elements inside this element for the same.
                    if (Patterns.Videos.IsMatch(element.InnerHtml))
                        return false;
                }
                return true;
            });
        }

        /**
         * Check if a given node has one of its ancestor tag name matching the
         * provided one.
         * @param  HTMLElement node
         * @param  String      tagName
         * @param  Number      maxDepth
         * @param  Function    filterFn a filter to invoke to determine whether this node "counts"
         * @return Boolean
         */
        bool _hasAncestorTag(HtmlNode node, string tagName, int maxDepth, Func<HtmlNode, bool> filterFn)
        {
            tagName = tagName.ToUpper();
            var depth = 0;
            while (node.ParentNode != null)
            {
                if (maxDepth > 0 && depth > maxDepth)
                    return false;
                if (node.ParentNode.Name == tagName && filterFn((HtmlNode)node.ParentNode))
                    return true;
                node = (HtmlNode)node.ParentNode;
                depth++;
            }
            return false;
        }


        /**
         * Return an object indicating how many rows and columns this table has.
         */
        (int rows, int columns) _getRowAndColumnCount(HtmlNode table)
        {
            var rows = 0;
            var columns = 0;
            var trs = table.GetElementsByTagName("tr");
            foreach (var tr in trs)
            {

                var rowspan = Convert<int>(tr.Attributes["rowspan"]?.Value);

                rows += rowspan;

                // Now look for column-related info
                var columnsInThisRow = 0;
                var cells = tr.GetElementsByTagName("td");

                foreach (var cell in cells)
                {
                    var colspan = Convert<int>(tr.Attributes["colspan"]?.Value);
                    columnsInThisRow += colspan;
                }

                columns = Math.Max(columns, columnsInThisRow);
            }
            return (rows, columns);
        }


        private T Convert<T>(string value)
        {
            try
            {
                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter != null)
                {
                    // Cast ConvertFromString(string text) : object to (T)
                    return (T)converter.ConvertFromString(value);
                }
                return default(T);
            }
            catch (NotSupportedException)
            {
                return default(T);
            }
        }


        /**
         * Look for "data" (as opposed to "layout") tables, for which we use
         * similar checks as
         * https://dxr.mozilla.org/mozilla-central/rev/71224049c0b52ab190564d3ea0eab089a159a4cf/accessible/html/HTMLTableAccessible.cpp#920
         */
        void _markDataTables(HtmlNode eroot)
        {
            var tables = eroot.GetElementsByTagName("table");
            foreach (var table in tables)
            {
                var role = table.Attributes["role"]?.Value;
                if (role == "presentation")
                {

                    table.SetAttributeValue("READABILITY_IsDataTable", false.ToString());

                    continue;
                }

                var datatable = Convert<int>(table.Attributes["datatable"]?.Value);
                if (datatable == 0)
                {
                    table.SetAttributeValue("READABILITY_IsDataTable", false.ToString());

                    continue;
                }
                var summary = table.Attributes["summary"];
                if (summary == null)
                {

                    table.SetAttributeValue("READABILITY_IsDataTable", true.ToString());

                    continue;
                }

                var caption = table.GetElementsByTagName("caption").First();
                if (caption != null && caption.ChildNodes.Count() > 0)
                {
                    table.SetAttributeValue("READABILITY_IsDataTable", true.ToString());

                    continue;
                }

                // If the table has a descendant with any of these tags, consider a data table:
                var dataTableDescendants = new string[] { "col", "colgroup", "tfoot", "thead", "th" };

                bool descendantExists(string tag)
                {
                    return table.GetElementsByTagName(tag).First() != null;
                };

                if (dataTableDescendants.Any(p => descendantExists(p)))
                {
                    Debug.WriteLine("Data table because found data-y descendant");
                    table.SetAttributeValue("READABILITY_IsDataTable", true.ToString());
                    continue;
                }

                // Nested tables indicate a layout table:
                if (table.GetElementsByTagName("table").Any())
                {
                    table.SetAttributeValue("READABILITY_IsDataTable", false.ToString());
                    continue;
                }

                var sizeInfo = this._getRowAndColumnCount(table);
                if (sizeInfo.rows >= 10 || sizeInfo.columns > 4)
                {
                    table.SetAttributeValue("READABILITY_IsDataTable", true.ToString());
                    continue;
                }
                // Now just go by size entirely:
                var idt = sizeInfo.rows * sizeInfo.columns > 10;
                table.SetAttributeValue("READABILITY_IsDataTable", idt.ToString());

            }
        }

        /**
         * Clean an element of all tags of type "tag" if they look fishy.
         * "Fishy" is an algorithm based on content Length, classnames, link density, number of images & embeds, etc.
         *
         * @return void
         **/
        void _cleanConditionally(HtmlNode e, string tag)
        {
            if (!Options.HasFlag(ParserFlags.FLAG_CLEAN_CONDITIONALLY))
                return;

            var isList = tag == "ul" || tag == "ol";

            // Gather counts for other typical elements embedded within.
            // Traverse backwards so we can remove nodes at the same time
            // without effecting the traversal.
            //
            // TODO: Consider taking into account original contentScore here.
            this._removeNodes(e.GetElementsByTagName(tag), (node) =>
            {
                // First check if we"re in a data table, in which case don"t remove us.
                if (this._hasAncestorTag(node, "table", -1, p => p.GetRDProperty<bool>("IsDataTable", "false")))
                {
                    return false;
                }

                var weight = this._getClassWeight(node);
                var contentScore = 0;

                Debug.WriteLine("Cleaning Conditionally", node);

                if (weight + contentScore < 0)
                {
                    return true;
                }

                if (this._getCharCount(node, ",") < 10)
                {
                    // If there are not very many commas, and the number of
                    // non-paragraph elements is more than paragraphs or other
                    // ominous signs, remove the element.
                    var p = node.GetElementsByTagName("p").Count();
                    var img = node.GetElementsByTagName("img").Count();
                    var li = node.GetElementsByTagName("li").Count() - 100;
                    var input = node.GetElementsByTagName("input").Count();

                    var embedCount = 0;
                    var embeds = node.GetElementsByTagName("embed");
                    foreach (var embed in embeds)
                    {
                        if (!Patterns.Videos.IsMatch(embed.InnerHtml))
                            embedCount += 1;
                    }

                    var linkDensity = this._getLinkDensity(node);
                    var contentLength = this._getInnerText(node).Length;

                    var haveToRemove =
                      (img > 1 && p / img < 0.5 && !this._hasAncestorTag(node, "figure", 3, px => true) ||
                      (!isList && li > p) ||
                      (input > Math.Floor((decimal)p / 3)) ||
                      (!isList && contentLength < 25 && (img == 0 || img > 2) && !this._hasAncestorTag(node, "figure", 3, px => true)) ||
                      (!isList && weight < 25 && linkDensity > 0.2) ||
                      (weight >= 25 && linkDensity > 0.5) ||
                      ((embedCount == 1 && contentLength < 75) || embedCount > 1));
                    return haveToRemove;
                }
                return false;
            });
        }

        /**
         * Clean out elements whose id/class combinations match specific string.
         *
         * @param Element
         * @param RegExp match id/class combination.
         * @return void
         **/
        void _cleanMatchedNodes(HtmlNode e, Regex regex)
        {
            var endOfSearchMarkerNode = this._getNextNode(e, true);
            var next = this._getNextNode(e, false);
            while (next != null && next != endOfSearchMarkerNode)
            {
                if (regex.IsMatch(next.Attributes["class"]?.Value + " " + next.Attributes["id"]?.Value))
                {
                    next = this._removeAndGetNext(next);
                }
                else
                {
                    next = this._getNextNode(next, false);
                }
            }
        }

        /**
         * Clean out spurious headers from an Element. Checks things like classnames and link density.
         *
         * @param Element
         * @return void
        **/
        void _cleanHeaders(HtmlNode e)
        {
            for (var headerIndex = 1; headerIndex < 3; headerIndex += 1)
            {
                this._removeNodes(e.GetElementsByTagName("h" + headerIndex), (header) =>
                {
                    return this._getClassWeight(header) < 0;
                });
            }
        }



        /**
         * Decides whether or not the document is reader-able without parsing the whole thing.
         *
         * @return boolean Whether or not we suspect parse() will suceeed at returning an article object.
         */
        bool isProbablyReaderable(bool helperIsVisible)
        {
            var nodes = this._getAllNodesWithTag((HtmlNode)doc, "p", "pre");

            // Get <div> nodes which have <br> node(s) and append them into the `nodes` variable.
            // Some articles' DOM structures might look like
            // <div>
            //   Sentences<br>
            //   <br>
            //   Sentences<br>
            // </div>
            var brNodes = this._getAllNodesWithTag((HtmlNode)doc, "div > br");
            if (brNodes.Count() > 0)
            {
                var set = new List<HtmlNode>();
                foreach (var node in brNodes)
                {
                    set.Add((HtmlNode)node.ParentNode);
                }
                nodes = set.Concat(nodes).ToList();
            }

            // FIXME we should have a fallback for helperIsVisible, but this is
            // problematic because of jsdom's elem.style handling - see
            // https://github.com/mozilla/readability/pull/186 for context.

            var score = 0d;

            foreach (var node in nodes)
            {
                var matchString = node.Attributes["class"].Value + " " + node.Attributes["id"].Value;

                if (Patterns.UnlikelyCandidates.IsMatch(matchString) &&
                   !Patterns.MaybeCandidates.IsMatch(matchString))
                {
                    continue;
                }

                //  if (node.matches && node.matches("li p")) {
                //     return false;
                //   }

                var textContentLength = node.InnerText.Trim().Length;

                if (textContentLength < 140)
                {
                    continue;
                }

                score += Math.Sqrt((double)textContentLength - 140.01);



            }

            if (score > 20)
                return true;
            else
                return false;

        }


        /**
         * Initialize a node with the readability object. Also checks the
         * className/id for special names to add to its score.
         *
         * @param Element
         * @return void
        **/
        void _initializeNode(HtmlNode node)
        {
            node.SetRDProperty("Initialized", true);
            node.SetRDProperty("ContentScore", 0x0);

            switch (node.Name.ToUpperInvariant())
            {
                case "DIV":
                    node.SetRDProperty("ContentScore", node.GetRDProperty<int>("ContentScore") + 5);
                    break;

                case "PRE":
                case "TD":
                case "BLOCKQUOTE":
                    node.SetRDProperty("ContentScore", node.GetRDProperty<int>("ContentScore") + 3);
                    break;

                case "ADDRESS":
                case "OL":
                case "UL":
                case "DL":
                case "DD":
                case "DT":
                case "LI":
                case "FORM":
                    node.SetRDProperty("ContentScore", node.GetRDProperty<int>("ContentScore") - 3);
                    break;

                case "H1":
                case "H2":
                case "H3":
                case "H4":
                case "H5":
                case "H6":
                case "TH":
                    node.SetRDProperty("ContentScore", node.GetRDProperty<int>("ContentScore") - 5);
                    break;
            }


            node.SetRDProperty("ContentScore", node.GetRDProperty<int>("ContentScore") - this._getClassWeight(node));

        }


        HtmlNode _removeAndGetNext(HtmlNode node)
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
        HtmlNode _getNextNode(HtmlNode node, bool ignoreSelfAndKids)
        {
            // First check for kids if those aren't being ignored
            if (!ignoreSelfAndKids && node.FirstChild != null)
            {
                return (HtmlNode)node.FirstChild;
            }
            // Then for siblings...
            if (node.NextSibling != null)
            {
                return (HtmlNode)node.NextSibling;
            }
            // And finally, move up the parent chain *and* find a sibling
            // (because this is depth-first traversal, we will have already
            // seen the parent nodes themselves).
            do
            {
                node = (HtmlNode)node.ParentNode;
            } while (node != null && node.NextSibling != null);
            return node != null ? (HtmlNode)node : (HtmlNode)node.NextSibling;
        }



        /***
         * grabArticle - Using a variety of metrics (content score, classname, element types), find the content that is
         *         most likely to be the stuff a user wants to read. Then return it wrapped up in a div.
         *
         * @param page a document to run upon. Needs to be a full document, complete with body.
         * @return Element
        **/
        HtmlNode _grabArticle()
        {
            Debug.WriteLine("**** grabArticle ****");


            var page = doc;

            // We can"t grab an article if we don"t have a page!
            if (page == null)
            {
                Debug.WriteLine("No body found in document. Abort.");
                return null;
            }

            var pageCacheHtml = page.InnerHtml;

            while (true)
            {
                var stripUnlikelyCandidates = Options.HasFlag(ParserFlags.FLAG_STRIP_UNLIKELYS);

                // First, node prepping. Trash nodes that look cruddy (like ones with the
                // class name "comment", etc), and turn divs into P tags where they have been
                // used inappropriately (as in, where they contain no other block level elements.)
                var elementsToScore = new List<HtmlNode>();
                var node = (HtmlNode)page;

                while (node != null)
                {
                    var matchString = node.Attributes["class"].Value + " " + node.Attributes["id"].Value;

                    // Check to see if this node is a byline, and remove it if it is.
                    if (this._checkByline(node, matchString))
                    {
                        node = this._removeAndGetNext(node);
                        continue;
                    }

                    // Remove unlikely candidates
                    if (stripUnlikelyCandidates)
                    {
                        if (Patterns.UnlikelyCandidates.IsMatch(matchString) &&
                            !Patterns.MaybeCandidates.IsMatch(matchString) &&
                            node.Name.ToUpper() != "BODY" &&
                            node.Name.ToUpper() != "A")
                        {
                            Debug.WriteLine("Removing unlikely candidate - " + matchString);
                            node = this._removeAndGetNext(node);
                            continue;
                        }
                    }

                    // Remove DIV, SECTION, and HEADER nodes without any content(e.g. text, image, video, or iframe).
                    if ((node.Name == "DIV" || node.Name == "SECTION" || node.Name == "HEADER" ||
                         node.Name == "H1" || node.Name == "H2" || node.Name == "H3" ||
                         node.Name == "H4" || node.Name == "H5" || node.Name == "H6") &&
                        this._isElementWithoutContent(node))
                    {
                        node = this._removeAndGetNext(node);
                        continue;
                    }

                    if (this.DEFAULT_TAGS_TO_SCORE.Contains(node.Name))
                    {
                        elementsToScore.Add(node);
                    }

                    // Turn all divs that don"t have children block level elements into p"s
                    if (node.Name == "DIV")
                    {
                        // Sites like http://mobile.slate.com encloses each paragraph with a DIV
                        // element. DIVs with only a P element inside and no text content can be
                        // safely converted into plain P elements to avoid confusing the scoring
                        // algorithm with DIVs with are, in practice, paragraphs.
                        if (this._hasSinglePInsideElement(node))
                        {
                            var newNode = node.ChildNodes[0];
                            node.ParentNode.ReplaceChild(newNode, node);
                            node = (HtmlNode)newNode;
                            elementsToScore.Add(node);
                        }
                        else if (!this._hasChildBlockElement(node))
                        {
                            node.Name = "P";
                            elementsToScore.Add(node);
                        }
                        else
                        {
                            // EXPERIMENTAL
                            foreach (var childNode in node.ChildNodes)
                            {
                                if (childNode.NodeType == HtmlNodeType.Text &&
                                    childNode.InnerText.Trim().Length > 0)
                                {
                                    var p = root.CreateElement("p");
                                    p.InnerHtml = childNode.InnerText;
                                    //p.style.display = "inline";
                                    p.AddClass("readability-styled");
                                    node.ReplaceChild(p, childNode);
                                }
                            }
                        }
                    }
                    node = this._getNextNode(node, false);
                }

                /**
                 * Loop through all paragraphs, and assign a score to them based on how content-y they look.
                 * Then add their score to their parent node.
                 *
                 * A score is determined by things like number of commas, class names, etc. Maybe eventually link density.
                **/
                // var candidates = [];
                var candidates = new List<HtmlNode>();

                foreach (var elementToScore in elementsToScore)
                {
                    if (elementToScore.ParentNode == null)
                        continue;

                    // If this paragraph is less than 25 characters, don't even count it.
                    var innerText = this._getInnerText(elementToScore);
                    if (innerText.Length < 25)
                        continue;

                    // Exclude nodes with no ancestor.
                    var ancestors = this._getNodeAncestors(elementToScore, 3);
                    if (ancestors.Count() == 0)
                        continue;

                    double contentScore = 0;

                    // Add a point for the paragraph itself as a base.
                    contentScore += 1;

                    // Add points for any commas within this paragraph.
                    contentScore += innerText.Split(',').Count();

                    // For every 100 characters in this paragraph, add another point. Up to 3 points.
                    contentScore += Math.Min(Math.Floor((double)innerText.Length / 100), 3);

                    // Initialize and score ancestors.
                    foreach (var ancestor in ancestors)
                    {
                        if (ancestor.Name.Length > 0 || ancestor.ParentNode != null)
                            continue;

                        if (!ancestor.GetRDProperty<bool>("Initialized"))
                        {
                            this._initializeNode(ancestor);
                            candidates.Add(ancestor);
                        }

                        // Node score divider:
                        // - parent:             1 (no division)
                        // - grandparent:        2
                        // - great grandparent+: ancestor level * 3
                        var scoreDivider = 1;
                        if (ancestor.GetRDProperty<int>("Level") == 0)
                            scoreDivider = 1;
                        else if (ancestor.GetRDProperty<int>("Level") == 1)
                            scoreDivider = 2;
                        else
                            scoreDivider = ancestor.GetRDProperty<int>("Level") * 3;

                        node.SetRDProperty("ContentScore", node.GetRDProperty<int>("ContentScore") + contentScore / scoreDivider);

                    }
                }

                // After we've calculated scores, loop through all of the possible
                // candidate nodes we found and find the one with the highest score.

                var topCandidates = new List<HtmlNode>();

                foreach (var candidate in candidates)
                {

                    // Scale the final candidates score based on link density. Good content
                    // should have a relatively small link density (5% or less) and be mostly
                    // unaffected by this operation.
                    var candidateScore = candidate.GetRDProperty<int>("ContentScore") * (1 - this._getLinkDensity(candidate));
                    candidate.SetRDProperty("ContentScore", candidateScore);

                    Debug.WriteLine("Candidate:", candidate, "with score " + candidateScore);

                    for (var t = 0; t < DEFAULT_N_TOP_CANDIDATES; t++)
                    {
                        var aTopCandidate = topCandidates[t];

                        if (aTopCandidate != null || candidateScore > aTopCandidate.GetRDProperty<int>("ContentScore"))
                        {
                            topCandidates.Insert(t, candidate);
                            if (topCandidates.Count() > DEFAULT_N_TOP_CANDIDATES)
                                topCandidates.Remove(topCandidates.Last());
                            break;
                        }
                    }
                }

                var topCandidate = topCandidates[0];

                var neededToCreateTopCandidate = false;

                HtmlNode parentOfTopCandidate;

                // If we still have no top candidate, just use the body as a last resort.
                // We also have to copy the body node so it is something we can modify.
                if (topCandidate == null || topCandidate.Name == "BODY")
                {
                    // Move all of the page's children into topCandidate
                    topCandidate = (HtmlNode)root.CreateElement("DIV");
                    neededToCreateTopCandidate = true;
                    // Move everything (not just elements, also text nodes etc.) into the container
                    // so we even include text directly in the body:


                    foreach (var kids in page.ChildNodes)
                    {
                        Debug.WriteLine("Moving child out:", kids);
                        topCandidate.AppendChild(kids);
                    }

                    page.AppendChild(topCandidate);

                    this._initializeNode(topCandidate);
                }
                else
                {
                    // Find a better top candidate node if it contains (at least three) nodes which belong to `topCandidates` array
                    // and whose scores are quite closed with current `topCandidate` node.
                    var alternativeCandidateAncestors = new List<List<HtmlNode>>();

                    for (var i = 1; i < topCandidates.Count(); i++)
                    {
                        if (topCandidates[i].GetRDProperty<int>("ContentScore") / topCandidate.GetRDProperty<int>("ContentScore") >= 0.75)
                        {

                            alternativeCandidateAncestors.Add(this._getNodeAncestors(topCandidates[i], 3));
                        }
                    }
                    var MINIMUM_TOPCANDIDATES = 3;
                    if (alternativeCandidateAncestors.Count() >= MINIMUM_TOPCANDIDATES)
                    {
                        parentOfTopCandidate = (HtmlNode)topCandidate.ParentNode;
                        while (parentOfTopCandidate.Name != "BODY")
                        {
                            var listsContainingThisAncestor = 0;
                            for (var ancestorIndex = 0; ancestorIndex <
                             alternativeCandidateAncestors.Count && listsContainingThisAncestor < MINIMUM_TOPCANDIDATES; ancestorIndex++)
                            {
                                listsContainingThisAncestor += (alternativeCandidateAncestors[ancestorIndex].Contains(parentOfTopCandidate)) ? 1 : 0;
                            }
                            if (listsContainingThisAncestor >= MINIMUM_TOPCANDIDATES)
                            {
                                topCandidate = parentOfTopCandidate;
                                break;
                            }
                            parentOfTopCandidate = (HtmlNode)parentOfTopCandidate.ParentNode;
                        }
                    }
                    if (!topCandidate.GetRDProperty<bool>("Initialized"))
                    {
                        this._initializeNode(topCandidate);
                    }

                    // Because of our bonus system, parents of candidates might have scores
                    // themselves. They get half of the node. There won't be nodes with higher
                    // scores than our topCandidate, but if we see the score going *up* in the first
                    // few steps up the tree, that's a decent sign that there might be more content
                    // lurking in other places that we want to unify in. The sibling stuff
                    // below does some of that - but only if we've looked high enough up the DOM
                    // tree.
                    parentOfTopCandidate = (HtmlNode)topCandidate.ParentNode;
                    var lastScore = topCandidate.GetRDProperty<int>("ContentScore");
                    // The scores shouldn't get too low.
                    var scoreThreshold = lastScore / 3;
                    while (parentOfTopCandidate.Name != "BODY")
                    {
                        if (!parentOfTopCandidate.GetRDProperty<bool>("Initialized"))
                        {
                            parentOfTopCandidate = (HtmlNode)parentOfTopCandidate.ParentNode;
                            continue;
                        }
                        var parentScore = parentOfTopCandidate.GetRDProperty<int>("ContentScore");
                        if (parentScore < scoreThreshold)
                            break;
                        if (parentScore > lastScore)
                        {
                            // Alright! We found a better parent to use.
                            topCandidate = parentOfTopCandidate;
                            break;
                        }
                        lastScore = parentOfTopCandidate.GetRDProperty<int>("ContentScore");
                        parentOfTopCandidate = (HtmlNode)parentOfTopCandidate.ParentNode;
                    }

                    // If the top candidate is the only child, use parent instead. This will help sibling
                    // joining logic when adjacent content is actually located in parent's sibling node.
                    parentOfTopCandidate = (HtmlNode)topCandidate.ParentNode;
                    while (parentOfTopCandidate.Name != "BODY" && parentOfTopCandidate.ChildNodes.Count() == 1)
                    {
                        topCandidate = parentOfTopCandidate;
                        parentOfTopCandidate = (HtmlNode)topCandidate.ParentNode;
                    }
                    if (!topCandidate.GetRDProperty<bool>("Initialized"))
                    {
                        this._initializeNode(topCandidate);
                    }
                }

                // Now that we have the top candidate, look through its siblings for content
                // that might also be related. Things like preambles, content split by ads
                // that we removed, etc.
                var articleContent = root.CreateElement("DIV");
                articleContent.SetAttributeValue("id", "readability-content");

                var siblingScoreThreshold = Math.Max(10, topCandidate.GetRDProperty<int>("ContentScore") * 0.2);
                // Keep potential top candidate's parent node to try to get text direction of it later.
                parentOfTopCandidate = (HtmlNode)topCandidate.ParentNode;
                var siblings = parentOfTopCandidate.ChildNodes.Cast<HtmlNode>().ToArray();

                for (int s = 0, sl = siblings.Count(); s < sl; s++)
                {
                    var sibling = siblings[s];
                    var append = false;

                    Debug.WriteLine("Looking at sibling node:", sibling, sibling.GetRDProperty<bool>("Initialized") ? ("with score " + sibling.GetRDProperty<int>("ContentScore")) : "");
                    Debug.WriteLine("Sibling has score", sibling.GetRDProperty<bool>("Initialized") ? sibling.GetRDProperty<int>("ContentScore").ToString() : "Unknown");

                    if (sibling == topCandidate)
                    {
                        append = true;
                    }
                    else
                    {
                        double contentBonus = 0;

                        // Give a bonus if sibling nodes and top candidates have the example same classname
                        if (sibling.GetAttributeValue("class", "") == topCandidate.GetAttributeValue("class", "") && topCandidate.GetAttributeValue("class", "") != "")
                            contentBonus += topCandidate.GetRDProperty<int>("ContentScore") * 0.2;

                        if (sibling.GetRDProperty<bool>("Initialized") &&
                            ((sibling.GetRDProperty<int>("ContentScore") + contentBonus) >= siblingScoreThreshold))
                        {
                            append = true;
                        }
                        else if (sibling.Name == "P")
                        {
                            var linkDensity = this._getLinkDensity(sibling);
                            var nodeContent = this._getInnerText(sibling);
                            var nodeLength = nodeContent.Count();

                            if (nodeLength > 80 && linkDensity < 0.25)
                            {
                                append = true;
                            }
                            else if (nodeLength < 80 && nodeLength > 0 && linkDensity == 0 &&
                                     Patterns.GrabArticle1.IsMatch(nodeContent))
                            {
                                append = true;
                            }
                        }
                    }

                    if (append)
                    {
                        Debug.WriteLine("Appending node:", sibling);

                        if (this.ALTER_TO_DIV_EXCEPTIONS.Contains(sibling.Name))
                        {
                            // We have a node that isn't a common block level element, like a form or td tag.
                            // Turn it into a div so it doesn't get filtered out later by accident.
                            Debug.WriteLine("Altering sibling:", sibling, "to div.");

                            sibling.Name = "DIV";
                        }

                        articleContent.AppendChild(sibling);
                        // siblings is a reference to the children array, and
                        // sibling is removed from the array when we call appendChild().
                        // As a result, we must revisit this index since the nodes
                        // have been shifted.
                        s -= 1;
                        sl -= 1;
                    }
                }

                Debug.WriteLine("Article content pre-prep: " + articleContent.InnerHtml);
                // So we have all of the content that we need. Now we clean it up for presentation.
                this._prepArticle((HtmlNode)articleContent);
                Debug.WriteLine("Article content post-prep: " + articleContent.InnerHtml);

                if (neededToCreateTopCandidate)
                {
                    // We already created a fake div thing, and there wouldn't have been any siblings left
                    // for the previous loop, so there's no point trying to create a new div, and then
                    // move all the children over. Just assign IDs and class names here. No need to append
                    // because that already happened anyway.
                    topCandidate.SetAttributeValue("class", "page");
                    topCandidate.SetAttributeValue("id", "readability-page-1");
                }
                else
                {
                    var div = root.CreateElement("DIV");


                    div.SetAttributeValue("class", "page");
                    div.SetAttributeValue("id", "readability-page-1");
                    var children = articleContent.ChildNodes;

                    while (children.Count() > 0)
                    {
                        div.AppendChild(children[0]);
                    }

                    articleContent.AppendChild(div);
                }


                Debug.WriteLine("Article content after paging: " + articleContent.InnerHtml);

                var parseSuccessful = true;

                // Now that we've gone through the full algorithm, check to see if
                // we got any meaningful content. If we didn't, we may need to re-run
                // grabArticle with different flags set. This gives us a higher likelihood of
                // finding the content, and the sieve approach gives us a higher likelihood of
                // finding the -right- content.
                // var textLength = this._getInnerText((HtmlNode)articleContent, true).Length;
                // if (textLength < DEFAULT_CHAR_THRESHOLD)
                // {
                //     parseSuccessful = false;
                //     page.InnerHtml = pageCacheHtml;

                //     if (this._flagIsActive(this.FLAG_STRIP_UNLIKELYS))
                //     {
                //         this._removeFlag(this.FLAG_STRIP_UNLIKELYS);
                //         this._attempts.push({ articleContent: articleContent, textLength: textLength});
                //     }
                //     else if (this._flagIsActive(this.FLAG_WEIGHT_CLASSES))
                //     {
                //         this._removeFlag(this.FLAG_WEIGHT_CLASSES);
                //         this._attempts.push({ articleContent: articleContent, textLength: textLength});
                //     }
                //     else if (this._flagIsActive(this.FLAG_CLEAN_CONDITIONALLY))
                //     {
                //         this._removeFlag(this.FLAG_CLEAN_CONDITIONALLY);
                //         this._attempts.push({ articleContent: articleContent, textLength: textLength});
                //     }
                //     else
                //     {
                //         this._attempts.push({ articleContent: articleContent, textLength: textLength});
                //         // No luck after removing flags, just return the longest text we found during the different loops
                //         this._attempts.sort(function(a, b) {
                //             return a.textLength < b.textLength;
                //         });

                //         // But first check if we actually have something
                //         if (!this._attempts[0].textLength)
                //         {
                //             return null;
                //         }

                //         articleContent = this._attempts[0].articleContent;
                //         parseSuccessful = true;
                //     }
                // }

                if (parseSuccessful)
                {
                    // Find out text direction from ancestors of final top candidate.
                    var l1 = new List<HtmlNode> { parentOfTopCandidate, topCandidate };

                    var ancestors = l1.Concat(this._getNodeAncestors(parentOfTopCandidate, 3));

                    foreach (var ancestor in ancestors)
                    {
                        var articleDir = ancestor.GetAttributeValue("dir", null);
                        if (articleDir != null)
                        {
                            this._articleDir = new Uri(articleDir);
                        }
                    }

                    return (HtmlNode)articleContent;
                }
            }
        }

        /**
          * Runs readability.
          *
          * Workflow:
          *  1. Prep the document by removing script tags, css, etc.
          *  2. Build readability's DOM tree.
          *  3. Grab the article content from the current dom tree.
          *  4. Replace the current DOM tree with the new one.
          *  5. Read peacefully.
          *
          * @return void
          **/
        public ProcessedArticle parse()
        {
            // Avoid parsing too large documents, as per configuration option
            if (DEFAULT_MAX_ELEMS_TO_PARSE > 0)
            {
                var numTags = doc.GetElementsByTagName("*").Count();
                if (numTags > DEFAULT_MAX_ELEMS_TO_PARSE)
                {
                    throw new Exception("Aborting parsing document; " + numTags + " elements found");
                }
            }

            // if (typeof this._doc.documentElement.firstElementChild == "undefined") {
            //     this._getNextNode = this._getNextNodeNoElementProperties;
            // }

            // Remove script tags from the document.
            this._removeScripts(doc);
            this._prepDocument();

            var metadata = this._getArticleMetadata();
            this._articleTitle = metadata.title;

            var articleContent = this._grabArticle();
            if (articleContent == null)
                return null;

            Debug.WriteLine("Grabbed: " + articleContent.InnerHtml);

            this._postProcessContent(articleContent);

            // If we haven"t found an excerpt in the article"s metadata, use the article's
            // first paragraph as the excerpt. This is used for displaying a preview of
            // the article's content.
            if (metadata.excerpt == null || metadata.excerpt.Length < 1)
            {
                var paragraphs = articleContent.GetElementsByTagName("p");
                if (paragraphs.Count() > 0)
                {
                    metadata.excerpt = paragraphs.First().InnerText.Trim();
                }
            }

            var textContent = articleContent.InnerText;
            return new ProcessedArticle()
            {
                title = this._articleTitle,
                byline = metadata.byline == null ? metadata.byline : this._articleByline,
                dir = this._articleDir,
                content = articleContent.InnerHtml,
                textContent = textContent,
                length = textContent.Length,
                excerpt = metadata.excerpt,
            };
        }
    }

    public class ProcessedArticle
    {
        public ProcessedArticle()
        {
        }

        public string title { get; internal set; }
        public string byline { get; internal set; }
        public Uri dir { get; internal set; }
        public string content { get; internal set; }
        public object textContent { get; internal set; }
        public object length { get; internal set; }
        public string excerpt { get; internal set; }
    }
}


public static class SpliceExtension
{
    public static IEnumerable<T> Splice<T>(this IEnumerable<T> list, int offset, int count)
    {
        return list.Skip(offset).Take(count);
    }
}

public class ArticleMetadata
{
    public string excerpt { get; set; }
    public string byline { get; set; }
    public string title { get; set; }
}

public class Attempt
{
    public HtmlNode articleContent { get; set; }
    public uint textLength { get; set; }
}
