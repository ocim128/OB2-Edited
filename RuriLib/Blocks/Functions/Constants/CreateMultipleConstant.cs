using System.Collections.Generic;
using System.Threading.Tasks;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;

namespace RuriLib.Blocks.Functions.Constants
{
    [BlockCategory("Constants", "Blocks that allow to assign constant values to variables", "#9acd32")]
    public static class CreateMultipleConstant
    {
        [Block("")]
        public static async Task<bool> CreateMultiple(
            BotData data,
            string variableName1 = "",
            string value1 = "",
            string variableName2 = "",
            string value2 = "",
            string variableName3 = "",
            string value3 = "",
            string variableName4 = "",
            string value4 = "",
            string variableName5 = "",
            string value5 = "",
            string variableName6 = "",
            string value6 = "",
            string variableName7 = "",
            string value7 = "",
            string variableName8 = "",
            string value8 = "",
            string variableName9 = "",
            string value9 = "",
            string variableName10 = "",
            string value10 = "")
        {
            data.Logger.LogHeader();

            int createdCount = 0;
            var variablesToCreate = new List<(string name, string value)>();

            // Collect all valid variable/value pairs
            if (!string.IsNullOrWhiteSpace(variableName1))
                variablesToCreate.Add((variableName1, value1));
            if (!string.IsNullOrWhiteSpace(variableName2))
                variablesToCreate.Add((variableName2, value2));
            if (!string.IsNullOrWhiteSpace(variableName3))
                variablesToCreate.Add((variableName3, value3));
            if (!string.IsNullOrWhiteSpace(variableName4))
                variablesToCreate.Add((variableName4, value4));
            if (!string.IsNullOrWhiteSpace(variableName5))
                variablesToCreate.Add((variableName5, value5));
            if (!string.IsNullOrWhiteSpace(variableName6))
                variablesToCreate.Add((variableName6, value6));
            if (!string.IsNullOrWhiteSpace(variableName7))
                variablesToCreate.Add((variableName7, value7));
            if (!string.IsNullOrWhiteSpace(variableName8))
                variablesToCreate.Add((variableName8, value8));
            if (!string.IsNullOrWhiteSpace(variableName9))
                variablesToCreate.Add((variableName9, value9));
            if (!string.IsNullOrWhiteSpace(variableName10))
                variablesToCreate.Add((variableName10, value10));

            // Process each variable
            foreach (var (name, val) in variablesToCreate)
            {
                // Replace interpolated variables in the value
                string replacedValue = ReplaceInterpolatedVariables(data, val);

                // Set the variable in bot context without verbose logging
                data.Objects[name] = replacedValue;

                // Log only the constant value assignment (simplified logging)
                data.Logger.Log($"Set constant value '{replacedValue}' to variable '{name}'", LogColors.YellowGreen);

                createdCount++;
            }

            data.Logger.Log($"Successfully created {createdCount} constant variables", LogColors.YellowGreen);
            return createdCount > 0;
        }



        /// <summary>
        /// Replaces interpolated variables in a string value
        /// </summary>
        private static string ReplaceInterpolatedVariables(BotData data, string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Handle interpolated strings like <variable>
            var result = value;
            var matches = System.Text.RegularExpressions.Regex.Matches(value, @"<([^>]+)>");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var variableName = match.Groups[1].Value;
                if (data.Objects.ContainsKey(variableName))
                {
                    var variableValue = data.Objects[variableName]?.ToString() ?? "";
                    result = result.Replace(match.Value, variableValue);
                }
            }

            return result;
        }
    }
}
