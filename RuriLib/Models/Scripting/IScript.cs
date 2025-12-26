using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Models.Scripting
{
    /// <summary>
    /// Represents an executable script (either from Roslyn or a compiled assembly).
    /// </summary>
    public interface IScript
    {
        /// <summary>
        /// Runs the script and returns the captured variables (top-level fields of the script class).
        /// </summary>
        Task<Dictionary<string, object>> RunAsync(object globals, CancellationToken cancellationToken);

        /// <summary>
        /// Compiles the script and returns diagnostics. 
        /// For cached scripts, this returns an empty list or cached diagnostics.
        /// </summary>
        System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Compile(CancellationToken cancellationToken = default);
    }
}
