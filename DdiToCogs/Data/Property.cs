using System;
using System.Collections.Generic;
using System.Text;

namespace Colectica.Cogs.Data
{
    public class Property
    {
        public string Name { get; set; }

        public string DataType { get; set; }

        public string MinCardinality { get; set; }
        public string MaxCardinality { get; set; }

        public string Description { get; set; }



        public string DeprecatedNamespace { get; set; }
        public string DeprecatedElementOrAttribute { get; set; }
        public string DeprecatedChoiceGroup { get; set; }

        public override string ToString()
        {
            return $"{Name} - {DataType} - {MinCardinality}..{MaxCardinality}";
        }
    }
}
