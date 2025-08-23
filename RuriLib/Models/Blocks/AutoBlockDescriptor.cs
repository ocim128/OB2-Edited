using System;
using System.Collections.Generic;

namespace RuriLib.Models.Blocks
{
    public class AutoBlockDescriptor : BlockDescriptor
    {
        public bool Async { get; set; }

        /// <summary>
        /// Stores the original parameter types from the method signature.
        /// This is needed to determine the correct type conversion (e.g., List<string> vs string[]).
        /// </summary>
        public Dictionary<string, Type> OriginalParameterTypes { get; set; } = new Dictionary<string, Type>();
    }
}
