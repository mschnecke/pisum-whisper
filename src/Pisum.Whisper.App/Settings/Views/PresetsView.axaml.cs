namespace Pisum.Whisper.App.Settings.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

public sealed partial class PresetsView : UserControl
{
    public PresetsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
