using System;
using System.Collections.Generic;

namespace RuriLib.Models.Variables;

public class BoolVariable : Variable
{
    private readonly bool _value;

    public BoolVariable(bool value)
    {
        _value = value;
        Type = VariableType.Bool;
    }

    public override string AsString() => _value.ToString();

    public override int AsInt() => _value ? 1 : 0;

    public override bool AsBool() => _value;

    public override byte[] AsByteArray() => BitConverter.GetBytes(_value);

    public override float AsFloat() => _value ? 1 : 0;

    public override List<string> AsListOfStrings() => [AsString()];

    public override object AsObject() => _value;
}
