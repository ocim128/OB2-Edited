using System;
using System.Collections.Generic;
using System.Dynamic;

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

            foreach (var variable in data.Line.GetVariables())
            {
                inputDict.Add(
                    variable.Name,
                    data.ConfigSettings.DataSettings.UrlEncodeDataAfterSlicing
                        ? Uri.EscapeDataString(variable.AsString())
                        : variable.AsString());
            }

            // Add the original data line as DATA only if it doesn't already exist from slicing
            if (inputDict.TryGetValue("DATA", out var existingDataObj) && existingDataObj is string existingDataStr && string.IsNullOrEmpty(existingDataStr))
            {
                // Override empty slice with original data line
                var dataValue = data.ConfigSettings.DataSettings.UrlEncodeDataAfterSlicing
                    ? Uri.EscapeDataString(data.Line.Data)
                    : data.Line.Data;
                inputDict["DATA"] = dataValue;
            }
            else if (!inputDict.ContainsKey("DATA"))
            {
                var dataValue = data.ConfigSettings.DataSettings.UrlEncodeDataAfterSlicing
                    ? Uri.EscapeDataString(data.Line.Data)
                    : data.Line.Data;
                inputDict.Add("DATA", dataValue);
            }


        }
    }
}
