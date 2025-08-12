using System;
using System.Collections.Generic;

namespace RuriLib.Models.Variables
{
    public abstract class Variable
    {
        public string Name
        {
            get => _name;
            set => _name = string.IsNullOrWhiteSpace(value) ? "variable" : value.Trim();
        }
        private string _name = "variable";

        public bool MarkedForCapture { get; set; } = false;
        public VariableType Type { get; set; } = VariableType.String;

        protected InvalidCastException CastError(string target)
            => new InvalidCastException($"Variable '{Name}' of type {Type} cannot be converted to {target}");

        public virtual string AsString() => throw CastError("string");
        public virtual int AsInt() => throw CastError("int");
        public virtual float AsFloat() => throw CastError("float");
        public virtual bool AsBool() => throw CastError("bool");
        public virtual List<string> AsListOfStrings() => throw CastError("List<string>");
        public virtual Dictionary<string, string> AsDictionaryOfStrings() => throw CastError("Dictionary<string,string>");
        public virtual byte[] AsByteArray() => throw CastError("byte[]");
        public virtual object AsObject() => throw CastError("object");
    }
}
