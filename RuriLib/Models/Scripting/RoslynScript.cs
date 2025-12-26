using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Scripting;

namespace RuriLib.Models.Scripting
{
    public class RoslynScript : IScript
    {
        private readonly Script _script;

        public RoslynScript(Script script)
        {
            _script = script;
        }

        public async Task<Dictionary<string, object>> RunAsync(object globals, CancellationToken cancellationToken)
        {
            // Execute the script
            var state = await _script.RunAsync(globals, null, cancellationToken).ConfigureAwait(false);
            
            // Extract variables
            var variables = new Dictionary<string, object>();
            if (!state.Variables.IsDefaultOrEmpty)
            {
                foreach (var variable in state.Variables)
                {
                    variables[variable.Name] = variable.Value;
                }
            }
            return variables;
        }

        public System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Compile(CancellationToken cancellationToken = default)
        {
            return _script.Compile(cancellationToken);
        }
    }
}
