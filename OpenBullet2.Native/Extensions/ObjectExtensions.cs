using System;

namespace OpenBullet2.Native.Extensions
{
    public static class ObjectExtensions
    {
        /// <summary>
        /// Safely converts an object to enum T with minimal allocations and robust handling.
        /// Accepts string (name), numeric underlying values, and already-typed enums.
        /// Throws ArgumentException when conversion is not possible.
        /// </summary>
        public static T AsEnum<T>(this object obj) where T : Enum
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));

            // Fast-path if already the correct enum type
            if (obj is T already) return already;

            var enumType = typeof(T);

            // Handle numeric underlying types without boxing to string
            // Enum.GetUnderlyingType ensures correct width and sign handling
            var underlying = Enum.GetUnderlyingType(enumType);

            try
            {
                switch (obj)
                {
                    case string s:
                        // Use IgnoreCase to be user-friendly and avoid errors due to casing
                        return (T)Enum.Parse(enumType, s, ignoreCase: true);

                    case byte b when underlying == typeof(byte):
                        return (T)Enum.ToObject(enumType, b);
                    case sbyte sb when underlying == typeof(sbyte):
                        return (T)Enum.ToObject(enumType, sb);
                    case short sh when underlying == typeof(short):
                        return (T)Enum.ToObject(enumType, sh);
                    case ushort ush when underlying == typeof(ushort):
                        return (T)Enum.ToObject(enumType, ush);
                    case int i when underlying == typeof(int):
                        return (T)Enum.ToObject(enumType, i);
                    case uint ui when underlying == typeof(uint):
                        return (T)Enum.ToObject(enumType, ui);
                    case long l when underlying == typeof(long):
                        return (T)Enum.ToObject(enumType, l);
                    case ulong ul when underlying == typeof(ulong):
                        return (T)Enum.ToObject(enumType, ul);
                }

                // Fall back: try Convert.ChangeType for other numeric representations
                if (obj is IConvertible)
                {
                    var converted = Convert.ChangeType(obj, underlying);
                    if (converted is not null)
                        return (T)Enum.ToObject(enumType, converted);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is InvalidCastException)
            {
                // Normalize as ArgumentException to keep the API surface simple
                throw new ArgumentException($"Cannot convert value '{obj}' of type '{obj.GetType()}' to enum '{enumType.Name}'", ex);
            }

            throw new ArgumentException($"Unsupported value type '{obj.GetType()}' for enum conversion to '{enumType.Name}'");
        }
    }
}
