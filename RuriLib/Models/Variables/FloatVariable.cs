using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuriLib.Models.Variables;

public class FloatVariable : Variable
{
    private readonly float _value;

    public FloatVariable(float value)
    {
        _value = value;
        Type = VariableType.Float;
    }

    public override string AsString() => _value.ToString(CultureInfo.InvariantCulture);

    public override int AsInt() => (int)_value;

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
