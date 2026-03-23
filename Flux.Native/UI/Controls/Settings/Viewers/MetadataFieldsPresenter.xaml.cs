using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Flux.Native.Controls;

public partial class MetadataFieldsPresenter : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(MetadataFieldsPresenter));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public MetadataFieldsPresenter()
    {
        InitializeComponent();
    }
}
