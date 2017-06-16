using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace DdiToCogs
{
    public class XmlCustomResolver : XmlUrlResolver
    {

        public string TempReusableFile { get; set; }

        public override Uri ResolveUri(Uri baseUri, string relativeUri)
        {
            if(relativeUri == "reusable.xsd")
            {
                Uri uri = new Uri(new Uri("file://"), TempReusableFile);
                return base.ResolveUri(uri, relativeUri);
            }

            return base.ResolveUri(baseUri, relativeUri);
        }
    }
}
