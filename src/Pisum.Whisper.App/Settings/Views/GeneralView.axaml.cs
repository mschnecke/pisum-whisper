namespace Pisum.Whisper.App.Settings.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

public sealed partial class GeneralView : UserControl
{
    public GeneralView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
