using System.Collections.Generic;
using System.Linq;

namespace RuriLib.Models.Variables;

public class DictionaryOfStringsVariable : Variable
{
    private readonly Dictionary<string, string> _value;

    public DictionaryOfStringsVariable(Dictionary<string, string> value)
    {
        _value = value;
        Type = VariableType.DictionaryOfStrings;
    }

    public override string AsString() => _value == null
        ? "null"
        : "{" + string.Join(", ", AsListOfStrings().Select(static s => $"({s})")) + "}";

    public override List<string> AsListOfStrings() =>
        _value.Select(static kvp => $"{kvp.Key}, {kvp.Value}").ToList();

    public override Dictionary<string, string> AsDictionaryOfStrings()
        => _value;

    public override object AsObject() => _value;
}
