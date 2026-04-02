using System;
using System.Reflection;
using System.Text;

namespace RuriLib.Extensions
{
    public static class ExceptionExtensions
    {
        public static string PrettyPrint(this Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append($"{ex.GetType()}: {ex.Message}");

            while ((ex is AggregateException || ex is TargetInvocationException) && ex.InnerException is not null)
            {
                ex = ex.InnerException;
                sb.Append($" | {ex.GetType()}: {ex.Message}");
            }

            return sb.ToString();
        }
    }
}
