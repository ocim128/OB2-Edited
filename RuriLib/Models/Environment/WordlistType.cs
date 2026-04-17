using System;
using RuriLib.Functions.Parsing;

namespace RuriLib.Models.Environment
{
    public class WordlistType
    {
        /// <summary>A cached compiled regex for this WordlistType's Regex pattern.</summary>
        public System.Text.RegularExpressions.Regex CompiledRegex => RegexCache.GetOrCreate(Regex);

        /// <summary>The name of the Wordlist Type.</summary>
        public string Name { get; set; } = "Default";

        /// <summary>The regular expression that validates the input data.</summary>
        public string Regex { get; set; } = ".*";

        /// <summary>Whether to check if the regex successfully matches the input data.</summary>
        public bool Verify { get; set; } = false;

        /// <summary>The separator used for slicing the input data into a list of strings.</summary>
        public string Separator { get; set; } = ":";

        /// <summary>The list of names of the variable that will be created by slicing the input data.</summary>
        public string[] Slices { get; set; } = new string[] { "DATA" };

        /// <summary>Alias for the list of names of the variable that will be created by slicing the input data.</summary>
        public string[] SlicesAlias { get; set; } = Array.Empty<string>();
    }
}
