using System;

namespace RuriLib.Attributes
{
    /// <summary>
    /// Attribute used to decorate a parameter of a block method to indicate it should be initialized
    /// as a setting of type variable, optionally with the given default variable name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class Variable : Attribute
    {
        /// <summary>
        /// The default variable name to assign as input to this parameter, e.g. data.SOURCE.
        /// This is immutable to prevent accidental mutation after attribute construction.
        /// </summary>
        public string DefaultVariableName { get; }

        /// <summary>
        /// Initializes a new instance of the Variable attribute with no default variable name.
        /// </summary>
        public Variable()
        {
        }

        /// <summary>
        /// Initializes a new instance of the Variable attribute with the provided default variable name.
        /// </summary>
        /// <param name="defaultVariableName">A default variable name like "data.SOURCE".</param>
        public Variable(string defaultVariableName)
        {
            DefaultVariableName = defaultVariableName;
        }
    }
}
