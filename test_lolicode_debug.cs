// Test to see the actual transpiled C# code for the infinite loop
using System;
using System.Collections.Generic;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Configs;

public class TestLoliCodeDebug
{
    public static void Main()
    {
        var lolicode = @"
#testloop 
BLOCK:ConstantString 
  value = ""this is for loop testing"" 
  => VAR @constantStringOutput 
ENDBLOCK 

JUMP #testloop
";
        
        var settings = new ConfigSettings();
        var stack = Loli2StackTranspiler.Transpile(lolicode);
        var csharp = Stack2CSharpTranspiler.Transpile(stack, settings, false);
        
        Console.WriteLine("=== TRANSPLED C# CODE ===");
        Console.WriteLine(csharp);
    }
}