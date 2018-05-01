using System;

namespace ReadaScrub
{
    public class Article
    { 
        public string Title { get; set; }
        public string Content { get; set; }
        public bool Success { get; internal set; }
        public Uri Uri { get; internal set; }
    }
}