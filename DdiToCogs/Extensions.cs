using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DdiToCogs
{
    public static class Extensions
    {
        public static void AddValue<TKey, TValue>
            (this IDictionary<TKey, List<TValue>> dictionary, TKey key, TValue value)
        {
            List<TValue> values;
            if (!dictionary.TryGetValue(key, out values))
            {
                values = new List<TValue>();
                dictionary.Add(key, values);
            }
            values.Add(value);
        }

        public static string TrimEnd(this string input, string suffixToRemove, StringComparison comparisonType)
        {

            if (input != null && suffixToRemove != null && input.EndsWith(suffixToRemove, comparisonType))
            {
                return input.Substring(0, input.Length - suffixToRemove.Length);
            }
            else return input;
        }
    }
}
