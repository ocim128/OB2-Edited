using System;
using System.Dynamic;
using System.Linq.Expressions;

namespace RuriLib.Models;

/// <summary>
/// A dynamic object that acts as a placeholder for missing variables.
/// Returns empty/default values and itself for any member/indexer access to prevent runtime errors.
/// </summary>
public sealed class NullDynamic : DynamicObject
{
    /// <summary>
    /// The singleton instance of NullDynamic.
    /// </summary>
    public static NullDynamic Instance { get; } = new();

    private NullDynamic() { }

    /// <summary>
    /// Handles member access (e.g., nullVar.SomeProperty).
    /// Always returns the NullDynamic instance.
    /// </summary>
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = this;
        return true;
    }

    /// <summary>
    /// Handles indexer access (e.g., nullVar[0] or nullVar["key"]).
    /// Always returns the NullDynamic instance.
    /// </summary>
    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
    {
        result = this;
        return true;
    }

    /// <summary>
    /// Handles member assignment (e.g., nullVar.SomeProperty = value).
    /// Always succeeds but does nothing.
    /// </summary>
    public override bool TrySetMember(SetMemberBinder binder, object? value) => true;

    /// <summary>
    /// Handles indexer assignment (e.g., nullVar[0] = value).
    /// Always succeeds but does nothing.
    /// </summary>
    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value) => true;

    /// <summary>
    /// Handles conversions to other types.
    /// Returns appropriate empty/default values.
    /// </summary>
    public override bool TryConvert(ConvertBinder binder, out object result)
    {
        var targetType = binder.Type;

        if (targetType == typeof(string))
        {
            result = "";
            return true;
        }

        if (targetType == typeof(int))
        {
            result = 0;
            return true;
        }

        if (targetType == typeof(long))
        {
            result = 0L;
            return true;
        }

        if (targetType == typeof(float))
        {
            result = 0.0f;
            return true;
        }

        if (targetType == typeof(double))
        {
            result = 0.0;
            return true;
        }

        if (targetType == typeof(decimal))
        {
            result = 0.0m;
            return true;
        }

        if (targetType == typeof(bool))
        {
            result = false;
            return true;
        }

        if (targetType.IsValueType)
        {
            result = Activator.CreateInstance(targetType);
            return true;
        }

        // For reference types, return null or empty string based on type
        if (targetType == typeof(object))
        {
            result = "";
            return true;
        }

        result = null;
        return true;
    }

    /// <summary>
    /// String representation returns empty string.
    /// </summary>
    public override string ToString() => "";

    /// <summary>
    /// Handles binary operations (e.g., nullVar + something).
    /// Returns the NullDynamic instance for chaining.
    /// </summary>
    public override bool TryBinaryOperation(BinaryOperationBinder binder, object arg, out object result)
    {
        result = this;
        return true;
    }

    /// <summary>
    /// Handles unary operations (e.g., !nullVar).
    /// Returns appropriate default values.
    /// </summary>
    public override bool TryUnaryOperation(UnaryOperationBinder binder, out object result)
    {
        switch (binder.Operation)
        {
            case ExpressionType.Not:
                result = true; // !false = true
                return true;
            case ExpressionType.Negate:
                result = 0;
                return true;
            default:
                result = this;
                return true;
        }
    }

    /// <summary>
    /// Handles method invocations (e.g., nullVar.SomeMethod()).
    /// Returns the NullDynamic instance.
    /// </summary>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        result = this;
        return true;
    }

    /// <summary>
    /// Handles direct invocation (e.g., nullVar()).
    /// Returns the NullDynamic instance.
    /// </summary>
    public override bool TryInvoke(InvokeBinder binder, object?[]? args, out object? result)
    {
        result = this;
        return true;
    }
}