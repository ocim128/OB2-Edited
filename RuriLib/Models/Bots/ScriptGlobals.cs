using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Runtime.CompilerServices;

namespace RuriLib.Models.Bots
{
    /// <summary>
    /// Global variables accessible by the Roslyn script.
    /// </summary>
    public class ScriptGlobals
    {
        /// <summary>
        /// The data of the bot, such as the current DataLine or Proxy being used.
        /// </summary>
        public BotData data { get; private set; }

        /// <summary>
        /// The expando object where each field is a slice of the original data line.
        /// </summary>
        public dynamic input;

        /// <summary>
        /// The expando object where global variables are stored.
        /// </summary>
        public dynamic globals;

        public ScriptGlobals(BotData data, dynamic globals)
        {
            // Ensure data is never null to prevent runtime binding exceptions
            this.data = data ?? throw new ArgumentNullException(nameof(data), "BotData cannot be null in ScriptGlobals");
            this.globals = globals;

            input = new ExpandoObject();
            var inputDict = (IDictionary<string, object>)input;

            // Hoist setting locally to avoid repeated property walks
            var urlEncode = this.data.ConfigSettings.DataSettings.UrlEncodeDataAfterSlicing;

            // Populate variables with minimal allocations
            foreach (var variable in this.data.Line.GetVariables())
            {
                var name = variable.Name;
                if (string.IsNullOrEmpty(name)) continue;

                // Avoid double conversion
                var val = variable.AsString();
                if (urlEncode && val is not null)
                {
                    val = Uri.EscapeDataString(val);
                }

                // Use TryAdd-like pattern since Expando's IDictionary throws on duplicate keys
                if (!inputDict.ContainsKey(name))
                {
                    inputDict.Add(name, val ?? string.Empty);
                }
                else
                {
                    // If duplicated slice name occurs, last one wins
                    inputDict[name] = val ?? string.Empty;
                }
            }

            // Add original DATA if needed or empty
            if (TryGetNonEmptyString(inputDict, "DATA", out var existingData))
            {
                // Keep existing non-empty DATA
            }
            else
            {
                var dataValue = this.data.Line.Data ?? string.Empty;
                if (urlEncode && dataValue.Length != 0)
                {
                    dataValue = Uri.EscapeDataString(dataValue);
                }

                if (inputDict.ContainsKey("DATA"))
                    inputDict["DATA"] = dataValue;
                else
                    inputDict.Add("DATA", dataValue);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetNonEmptyString(IDictionary<string, object> dict, string key, out string value)
        {
            if (dict.TryGetValue(key, out var obj) && obj is string s && !string.IsNullOrEmpty(s))
            {
                value = s;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
