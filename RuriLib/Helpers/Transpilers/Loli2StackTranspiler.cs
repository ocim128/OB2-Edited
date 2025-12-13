using RuriLib.Exceptions;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks;
using System;
using System.Collections.Generic;
using System.Text;

namespace RuriLib.Helpers.Transpilers
{
    /// <summary>
    /// Takes care of transpiling LoliCode to a list of blocks.
    /// </summary>
    public static class Loli2StackTranspiler
    {

        /// <summary>
        /// Creates a list of <see cref="BlockInstance"/> objects from a LoliCode <paramref name="script"/>.
        /// </summary>
        public static List<BlockInstance> Transpile(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return new List<BlockInstance>();

            var lines = script.Split(new string[] { "\n", "\r\n" }, System.StringSplitOptions.None);
            var stack = new List<BlockInstance>();

            var localLineNumber = 0;
            var lineNumber = 0;

            while (localLineNumber < lines.Length)
            {
                var line = lines[localLineNumber];
                var trimmedSpan = line.AsSpan().Trim();
                localLineNumber++;
                lineNumber++;

                var isBlockDirective = TryParseBlockDirective(trimmedSpan, out var blockId);

                if (!isBlockDirective && trimmedSpan.StartsWith("BLOCK:", StringComparison.Ordinal))
                {
                    throw new LoliCodeParsingException(lineNumber, "Could not parse the block id");
                }

                if (isBlockDirective)
                {
                    var block = BlockFactory.GetBlock<BlockInstance>(blockId);
                    var sb = new StringBuilder();

                    while (localLineNumber < lines.Length)
                    {
                        line = lines[localLineNumber];
                        trimmedSpan = line.AsSpan().Trim();
                        localLineNumber++;

                        if (IsBlockTermination(trimmedSpan))
                            break;

                        sb.AppendLine(line);
                    }

                    var blockOptions = sb.ToString();
                    block.FromLC(ref blockOptions, ref lineNumber);
                    lineNumber++;

                    stack.Add(block);
                }
                else
                {
                    var descriptor = new LoliCodeBlockDescriptor();
                    var block = new LoliCodeBlockInstance(descriptor);

                    var sb = new StringBuilder();
                    var startingLineNumber = lineNumber;

                    sb.Append(line);

                    while (localLineNumber < lines.Length)
                    {
                        sb.AppendLine();
                        line = lines[localLineNumber];
                        trimmedSpan = line.AsSpan().Trim();
                        lineNumber++;
                        localLineNumber++;

                        if (TryParseBlockDirective(trimmedSpan, out _))
                        {
                            lineNumber--;
                            localLineNumber--;
                            break;
                        }

                        sb.Append(line);
                    }

                    var blockScript = sb.ToString();
                    var tempLineNumber = startingLineNumber;
                    block.FromLC(ref blockScript, ref tempLineNumber);

                    if (!string.IsNullOrWhiteSpace(block.Script))
                        stack.Add(block);
                }
            }

            return stack;
        }

        public static bool IsScriptDangerous(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return false;

            var lines = script.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            var localLineNumber = 0;
            while (localLineNumber < lines.Length)
            {
                var line = lines[localLineNumber].AsSpan().Trim();
                localLineNumber++;

                if (TryParseBlockDirective(line, out var blockId))
                {
                    // Check if the block ID is dangerous
                    if (blockId.Equals("Script", StringComparison.OrdinalIgnoreCase) || 
                        blockId.Equals("ShellCommand", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Skip the block body
                    while (localLineNumber < lines.Length)
                    {
                        var bodyLine = lines[localLineNumber].AsSpan().Trim();
                        localLineNumber++;

                        if (IsBlockTermination(bodyLine))
                        {
                            // If we encountered another block directive inside (nested?), TryParseBlockDirective handles it?
                            // No, Stack2Loli doesn't really support nested blocks in this flat parser in the same way.
                            // But usually ENDBLOCK terminates it.
                            break;
                        }
                        
                        // Check if we hit another BLOCK directive (implicit termination/error usually, but here we just want to skip)
                        if (TryParseBlockDirective(bodyLine, out _))
                        {
                            localLineNumber--;
                            break;
                        }
                    }
                }
                else
                {
                    // If it's not a block directive, it's raw LoliCode => Dangerous
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseBlockDirective(ReadOnlySpan<char> line, out string blockId)
        {
            blockId = string.Empty;

            if (line.Length == 0)
                return false;

            // Check starts with "BLOCK:"
            if (!line.StartsWith("BLOCK:", StringComparison.Ordinal))
                return false;

            var token = line.Slice("BLOCK:".Length).Trim();
            
            if (token.Length == 0 || !IsValidToken(token))
                return false;

            blockId = token.ToString();
            return true;
        }

        private static bool IsBlockTermination(ReadOnlySpan<char> line)
        {
            if (line.Length == 0)
                return false;

            return line.StartsWith("ENDBLOCK", StringComparison.Ordinal);
        }

        private static bool IsValidToken(ReadOnlySpan<char> token)
        {
            if (token.Length == 0)
                return false;

            if (!char.IsLetter(token[0]))
                return false;

            for (int i = 1; i < token.Length; i++)
            {
                var ch = token[i];
                if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                    return false;
            }

            return true;
        }
    }
}
