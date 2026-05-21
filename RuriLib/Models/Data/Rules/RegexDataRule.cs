using System.Text.RegularExpressions;
using RuriLib.Functions.Parsing;

namespace RuriLib.Models.Data.Rules
{
    public class RegexDataRule : DataRule
    {
        public string RegexToMatch { get; set; } = "^.*$";

        public override bool IsSatisfied(string value)
        {
            if (value is null)
            {
                throw new System.ArgumentNullException(nameof(value));
            }

            if (RegexToMatch == string.Empty)
            {
                return Invert ^ true;
            }

            try
            {
                return Invert ^ RegexCache.GetOrCreate(RegexToMatch).IsMatch(value);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
