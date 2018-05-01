using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using AngleSharp;

namespace ReadaScrub
{
    public class Parser
    {
        static HttpClient webClient = new HttpClient();

        private string UriString;
        public Uri BaseURI { get; private set; }
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
    }

}





