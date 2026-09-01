namespace Pisum.Whisper.App.Settings.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

public sealed partial class LoggingView : UserControl
{
    public LoggingView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
