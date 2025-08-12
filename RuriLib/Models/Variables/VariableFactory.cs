using System;
using System.Collections.Generic;
using System.Linq;

namespace RuriLib.Models.Variables
{
    public class VariableFactory
    {
        public static Variable FromObject(object obj)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj), "Cannot create a Variable from null");

            return obj switch
            {
                bool x => new BoolVariable(x),
                byte[] x => new ByteArrayVariable(x),
                Dictionary<string, string> x => new DictionaryOfStringsVariable(x),
                float x => new FloatVariable(x),
                double x => new FloatVariable((float)x),
                int x => new IntVariable(x),
                List<string> x => new ListOfStringsVariable(x),
                // Broader interface support
                IDictionary<string, string> x => new DictionaryOfStringsVariable(new Dictionary<string, string>(x)),
                IEnumerable<string> x => new ListOfStringsVariable(x.ToList()),
                string x => new StringVariable(x),
                _ => throw new NotSupportedException("Unsupported variable source type: " + obj.GetType().FullName)
            };
        }
    }
}
