using System;
using System.Collections.Generic;

namespace RuriLib.Models.Variables;

public class IntVariable : Variable
{
    private readonly int _value;

    public IntVariable(int value)
    {
        _value = value;
        Type = VariableType.Int;
    }

    public override string AsString() => _value.ToString();

    public override int AsInt() => _value;

    public override bool AsBool()
    {
        if (_value == 0)
        {
            return false;
        }

        return _value == 1 ? true : throw new InvalidCastException();
    }

    public override byte[] AsByteArray() => BitConverter.GetBytes(_value);

    public override float AsFloat() => _value;

    public override List<string> AsListOfStrings() => [AsString()];

    public override object AsObject() => _value;
}
