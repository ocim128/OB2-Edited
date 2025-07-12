using System.Dynamic;

namespace RuriLib.Helpers;

/// <summary>
/// A dynamic object that safely absorbs any member/index access and returns itself,
/// effectively acting as a forever-empty placeholder to prevent runtime binder exceptions.
/// ToString() returns an empty string so it can be used where a string is expected.
/// </summary>
public sealed class NullDynamic : DynamicObject
{
    public static readonly dynamic Instance = new NullDynamic();

    private NullDynamic() { }

    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = Instance;
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
    {
        result = Instance;
        return true;
    }

    public override bool TryInvoke(InvokeBinder binder, object?[]? args, out object? result)
    {
        result = Instance;
        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        result = Instance;
        return true;
    }

    public override string ToString() => string.Empty;
}