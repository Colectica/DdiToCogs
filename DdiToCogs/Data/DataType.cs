using System;
using System.Collections.Generic;

namespace Colectica.Cogs.Data
{
    public class DataType
    {

        public string Name { get; set; }
        public string Description { get; set; }

        public string Extends { get; set; }

        public bool IsAbstract { get; set; }
        public bool IsPrimitive { get; set; }

        public List<Property> Properties { get; set; } = new List<Property>();

        public string DeprecatedNamespace { get; set; }
        public bool IsDeprecated { get; set; }

        

    }
}
