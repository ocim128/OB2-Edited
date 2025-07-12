using System.Collections.Generic;

namespace RuriLib.Models.Variables;

public class ListOfStringsVariable : Variable
{
    private readonly List<string> _value;

    public ListOfStringsVariable(List<string> value)
    {
        _value = value;
        Type = VariableType.ListOfStrings;
    }

    public override string AsString() => _value == null
        ? "null"
        : "[" + string.Join(", ", _value) + "]";

    public override List<string> AsListOfStrings() => _value;

    public override object AsObject() => _value;
}
