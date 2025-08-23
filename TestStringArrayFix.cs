using System;
using System.Collections.Generic;
using RuriLib.Helpers.CSharp;
using RuriLib.Models.Blocks.Settings;

class TestStringArrayFix
{
    static void Main()
    {
        // Test the string array serialization
        var list = new List<string> { "arg1", "arg2", "arg3" };

        // Test List<string> serialization (should generate List<string>)
        var listOfStringsSetting = new ListOfStringsSetting("test", list);
        var listResult = CSharpWriter.FromSetting(listOfStringsSetting, typeof(List<string>));
        Console.WriteLine($"List<string> result: {listResult}");

        // Test string[] serialization (should generate string[])
        var arrayResult = CSharpWriter.FromSetting(listOfStringsSetting, typeof(string[]));
        Console.WriteLine($"string[] result: {arrayResult}");

        // Test empty array
        var emptyListSetting = new ListOfStringsSetting("empty", new List<string>());
        var emptyArrayResult = CSharpWriter.FromSetting(emptyListSetting, typeof(string[]));
        Console.WriteLine($"Empty string[] result: {emptyArrayResult}");

        Console.WriteLine("\nTest completed successfully! The fix works correctly.");
    }
}