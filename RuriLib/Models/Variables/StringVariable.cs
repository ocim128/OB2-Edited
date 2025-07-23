using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RuriLib.Models.Variables
{
    public class StringVariable : Variable
    {
        private string value;

        public StringVariable(string value)
        {
            this.value = value;
            Type = VariableType.String;
        }

        public override string AsString() => value;

        public override int AsInt()
        {
            if (int.TryParse(value, out int result))
                return result;
            else
                throw new InvalidCastException();
        }

        public override bool AsBool()
        {
            if (bool.TryParse(value, out bool result))
                return result;

            // Handle common boolean representations
            string trimmedValue = value?.Trim();
            if (string.IsNullOrEmpty(trimmedValue))
                return false;

            // Handle numeric strings
            if (int.TryParse(trimmedValue, out int intResult))
                return intResult != 0;

            // Handle common string representations
            var trueValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "true", "1", "yes", "y", "on", "enabled", "enable", "active"
            };

            var falseValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "false", "0", "no", "n", "off", "disabled", "disable", "inactive"
            };

            if (trueValues.Contains(trimmedValue))
                return true;

            if (falseValues.Contains(trimmedValue))
                return false;

            // For any other non-empty string, treat as true
            return true;
        }

        public override byte[] AsByteArray() => Encoding.UTF8.GetBytes(value);

        public override float AsFloat()
        {
            if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
                return result;
            else
                throw new InvalidCastException();
        }

        public override List<string> AsListOfStrings() => new List<string> { value };

        public override object AsObject() => value;
    }
}
