using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DdiToCogs
{
    public class TypedReference
    {
        public string ParentTypeName { get; set; }
        public string ReferenceName { get; set; }
        public string ItemTypeName { get; set; }


        public override string ToString()
        {
            return $"{ParentTypeName} : {ReferenceName} -> {ItemTypeName}";
        }
    }
}
