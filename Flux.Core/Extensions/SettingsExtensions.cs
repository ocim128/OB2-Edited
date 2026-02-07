using Flux.Core.Models.Settings;
using System.Reflection;

namespace Flux.Core.Extensions
{
    public static class SettingsExtensions
    {
        public static object GetProperty(this object obj, string propertyName)
            => obj.GetType().GetProperty(propertyName)?.GetValue(obj, null);

        public static void SetProperty(this object obj, string propertyName, object value)
        {
            var property = obj.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obj, value, null);
            }
        }
    }
} 