using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace RuriLib.Models.Scripting
{
    public class CompiledAssemblyScript : IScript
    {
        private readonly Assembly _assembly;
        private readonly Type _submissionType;

        public CompiledAssemblyScript(Assembly assembly)
        {
            _assembly = assembly;
            // The submission type is usually named "Submission#0"
            _submissionType = _assembly.GetExportedTypes().FirstOrDefault(t => t.Name.Contains("Submission#")) 
                              ?? _assembly.GetExportedTypes().FirstOrDefault();
            
            if (_submissionType == null)
                throw new InvalidOperationException("Could not find submission type in compiled assembly.");
        }

        public async Task<Dictionary<string, object>> RunAsync(object globals, CancellationToken cancellationToken)
        {
            // Replicate Roslyn script execution to capture variables
            var constructor = _submissionType.GetConstructors().FirstOrDefault();
            if (constructor == null)
                throw new InvalidOperationException("Could not find constructor for submission type.");

            // Initialize submission array with globals
            // Slot 0 is usually for globals. The array length must match what the script expects.
            // Typical Roslyn single submission expects at least 2 slots? Or just 1?
            // We'll try with a safe buffer.
            var submissionArray = new object[2]; 
            submissionArray[0] = globals;

            object instance;
            try
            {
                instance = constructor.Invoke(new object[] { submissionArray });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            // Find <Initialize> method (async entry point)
            var initializeMethod = _submissionType.GetMethod("<Initialize>", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (initializeMethod != null)
            {
                object result;
                try
                {
                    result = initializeMethod.Invoke(instance, null);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }

                if (result is Task t)
                {
                    await t.ConfigureAwait(false);
                }
            }

            // Extract variables (fields)
            var variables = new Dictionary<string, object>();
            foreach (var field in _submissionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                // Skip compiler generated fields (e.g. backing fields, state machine references)
                if (field.Name.Contains("<") || field.Name.Contains(">")) continue;
                
                variables[field.Name] = field.GetValue(instance);
            }
            
            return variables;
        }

        public System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Compile(CancellationToken cancellationToken = default)
        {
            return System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>.Empty;
        }
    }
}
