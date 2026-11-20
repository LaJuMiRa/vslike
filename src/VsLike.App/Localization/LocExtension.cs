using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace VsLike.App.Localization;

/// <summary>XAML: <c>Text="{l:Loc Menu.File}"</c>, aktualisiert sich bei Sprachwechsel.</summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = LocalizedStrings.Instance, Mode = BindingMode.OneWay };
}
