using System;
using System.Collections.Generic;
using RuriLib.Models;

namespace RuriLib.Extensions
{
    public static class ObjectExtensions
    {
        public static string DynamicAsString(this object obj) => obj switch
        {
            bool x => x.AsString(),
            byte[] x => x.AsString(),
            int x => x.AsString(),
            float x => x.AsString(),
            string x => x.AsString(),
            List<string> x => x.AsString(),
            Dictionary<string, string> x => x.AsString(),
            NullDynamic _ => "",
            null => "",
            _ => obj.ToString() ?? ""
        };

        public static int DynamicAsInt(this object obj) => obj switch
        {
            bool x => x.AsInt(),
            byte[] x => x.AsInt(),
            int x => x.AsInt(),
            float x => x.AsInt(),
            string x => x.AsInt(),
            List<string> x => x.AsInt(),
            // I have absolutely no idea why it wouldn't let me compile it with the extension method...
            Dictionary<string, string> x => DictionaryOfStringsExtensions.AsInt(x),
            NullDynamic _ => 0,
            null => 0,
            _ => 0
        };

        public static float DynamicAsFloat(this object obj) => obj switch
        {
            bool x => x.AsFloat(),
            byte[] x => x.AsFloat(),
            int x => x.AsFloat(),
            float x => x.AsFloat(),
            string x => x.AsFloat(),
            List<string> x => x.AsFloat(),
            // I have absolutely no idea why it wouldn't let me compile it with the extension method...
            Dictionary<string, string> x => DictionaryOfStringsExtensions.AsFloat(x),
            NullDynamic _ => 0.0f,
            null => 0.0f,
            _ => 0.0f
        };

        public static bool DynamicAsBool(this object obj) => obj switch
        {
            bool x => x.AsBool(),
            byte[] x => x.AsBool(),
            int x => x.AsBool(),
            float x => x.AsBool(),
            string x => x.AsBool(),
            List<string> x => x.AsBool(),
            // I have absolutely no idea why it wouldn't let me compile it with the extension method...
            Dictionary<string, string> x => DictionaryOfStringsExtensions.AsBool(x),
            NullDynamic _ => false,
            null => false,
            _ => false
        };

        public static List<string> DynamicAsList(this object obj) => obj switch
        {
            bool x => x.AsList(),
            byte[] x => x.AsList(),
            int x => x.AsList(),
            float x => x.AsList(),
            string x => x.AsList(),
            List<string> x => x.AsList(),
            Dictionary<string, string> x => x.AsList(),
            NullDynamic _ => new(),
            null => new(),
            _ => new()
        };

        public static Dictionary<string, string> DynamicAsDict(this object obj) => obj switch
        {
            bool x => x.AsDict(),
            byte[] x => x.AsDict(),
            int x => x.AsDict(),
            float x => x.AsDict(),
            string x => x.AsDict(),
            List<string> x => x.AsDict(),
            Dictionary<string, string> x => x.AsDict(),
            NullDynamic _ => new(),
            null => new(),
            _ => new()
        };

        public static byte[] DynamicAsBytes(this object obj) => obj switch
        {
            bool x => x.AsBytes(),
            byte[] x => x.AsBytes(),
            int x => x.AsBytes(),
            float x => x.AsBytes(),
            string x => x.AsBytes(),
            List<string> x => x.AsBytes(),
            Dictionary<string, string> x => x.AsBytes(),
            NullDynamic _ => Array.Empty<byte>(),
            null => Array.Empty<byte>(),
            _ => Array.Empty<byte>()
        };

        // Add general extension methods that can handle dynamic objects including NullDynamic
        // These will be used when the compiler can't determine the specific type

        public static string AsString(this object obj) => obj switch
        {
            bool x => x.AsString(),
            byte[] x => x.AsString(),
            int x => x.AsString(),
            float x => x.AsString(),
            string x => x.AsString(),
            List<string> x => x.AsString(),
            Dictionary<string, string> x => x.AsString(),
            NullDynamic _ => "",
            null => "",
            _ => obj.ToString() ?? ""
        };

        public static int AsInt(this object obj) => obj switch
        {
            bool x => x.AsInt(),
            byte[] x => x.AsInt(),
            int x => x.AsInt(),
            float x => x.AsInt(),
            string x => x.AsInt(),
            List<string> x => x.AsInt(),
            Dictionary<string, string> x => DictionaryOfStringsExtensions.AsInt(x),
            NullDynamic _ => 0,
            null => 0,
            _ => 0
        };

        public static float AsFloat(this object obj) => obj switch
        {
            bool x => x.AsFloat(),
            byte[] x => x.AsFloat(),
            int x => x.AsFloat(),
            float x => x.AsFloat(),
            string x => x.AsFloat(),
            List<string> x => x.AsFloat(),
            Dictionary<string, string> x => DictionaryOfStringsExtensions.AsFloat(x),
            NullDynamic _ => 0.0f,
            null => 0.0f,
            _ => 0.0f
        };

        public static bool AsBool(this object obj) => obj switch
        {
            bool x => x.AsBool(),
            byte[] x => x.AsBool(),
            int x => x.AsBool(),
            float x => x.AsBool(),
            string x => x.AsBool(),
            List<string> x => x.AsBool(),
            Dictionary<string, string> x => DictionaryOfStringsExtensions.AsBool(x),
            NullDynamic _ => false,
            null => false,
            _ => false
        };

        public static List<string> AsList(this object obj) => obj switch
        {
            bool x => x.AsList(),
            byte[] x => x.AsList(),
            int x => x.AsList(),
            float x => x.AsList(),
            string x => x.AsList(),
            List<string> x => x.AsList(),
            Dictionary<string, string> x => x.AsList(),
            NullDynamic _ => new(),
            null => new(),
            _ => new()
        };

        public static Dictionary<string, string> AsDict(this object obj) => obj switch
        {
            bool x => x.AsDict(),
            byte[] x => x.AsDict(),
            int x => x.AsDict(),
            float x => x.AsDict(),
            string x => x.AsDict(),
            List<string> x => x.AsDict(),
            Dictionary<string, string> x => x.AsDict(),
            NullDynamic _ => new(),
            null => new(),
            _ => new()
        };

        public static byte[] AsBytes(this object obj) => obj switch
        {
            bool x => x.AsBytes(),
            byte[] x => x.AsBytes(),
            int x => x.AsBytes(),
            float x => x.AsBytes(),
            string x => x.AsBytes(),
            List<string> x => x.AsBytes(),
            Dictionary<string, string> x => x.AsBytes(),
            NullDynamic _ => Array.Empty<byte>(),
            null => Array.Empty<byte>(),
            _ => Array.Empty<byte>()
        };
    }
}
