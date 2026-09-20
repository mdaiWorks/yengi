using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace mdaiAgent;

[MarkupExtensionReturnType(typeof(object))]
public class LocExtension : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public string? StringFormat { get; set; }

    public object? FallbackValue { get; set; }

    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return DependencyProperty.UnsetValue;

        var binding = new Binding
        {
            Source = LocalizationManager.Instance,
            Path = new PropertyPath($"[{Key}]"),
            Mode = BindingMode.OneWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };

        if (!string.IsNullOrEmpty(StringFormat))
            binding.StringFormat = StringFormat;

        if (FallbackValue != null)
            binding.FallbackValue = FallbackValue;

        return binding.ProvideValue(serviceProvider);
    }
}
