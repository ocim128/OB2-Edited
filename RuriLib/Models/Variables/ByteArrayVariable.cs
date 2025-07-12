using System;
using System.Collections.Generic;
using System.Text;

namespace RuriLib.Models.Variables;

public class ByteArrayVariable : Variable
{
    private readonly byte[] _value;

    public ByteArrayVariable(byte[] value)
    {
        _value = value;
        Type = VariableType.ByteArray;
    }

    public override string AsString() => _value == null ? "null" : Encoding.UTF8.GetString(_value);

    public override int AsInt() => BitConverter.ToInt32(_value, 0);

    public override bool AsBool() => BitConverter.ToBoolean(_value, 0);

    public override byte[] AsByteArray() => _value;

    public override float AsFloat() => AsInt();

    public override List<string> AsListOfStrings() => [AsString()];

    public override object AsObject() => _value;
}
