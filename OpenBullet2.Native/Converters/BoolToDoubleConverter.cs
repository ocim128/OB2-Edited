using System.Windows;
using System.Windows.Data;

namespace OpenBullet2.Native.Converters
{
    public sealed class BoolToDoubleConverter : BooleanConverter<double>
    {
        public BoolToDoubleConverter() :
            base(150.0, 0.0)
        {
        }
    }
}
