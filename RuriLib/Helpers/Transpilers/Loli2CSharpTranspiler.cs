using RuriLib.Models.Configs;
using RuriLib.Helpers.CSharp;

namespace RuriLib.Helpers.Transpilers
{
    /// <summary>
    /// Takes care of transpiling LoliCode to C#.
    /// </summary>
    public static class Loli2CSharpTranspiler
    {
        /// <summary>
        /// Transpiles a LoliCode script to a C# script string.
        /// You can use the <see cref="ScriptBuilder"/> to compile it to an executable script.
        /// </summary>
        public static string Transpile(string script, ConfigSettings settings, bool stepByStep = false)
        {
            // Use the fast transpiler if we don't need step-by-step debugging
            // This avoids creating the Block Stack and significantly improves performance
            if (!stepByStep)
            {
                return FastLoli2CSharpTranspiler.Transpile(script, settings);
            }

            var stack = Loli2StackTranspiler.Transpile(script);
            return Stack2CSharpTranspiler.Transpile(stack, settings, stepByStep);
        }
    }
}
