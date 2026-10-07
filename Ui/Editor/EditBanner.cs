using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OrbitWeave.Ui.Editor;

// Bandeau du mode Modifier : petite fenêtre fixée en haut au milieu de l'écran de la roue,
// quelle que soit la taille ou la place de la roue. Ses touches (Échap, Ctrl+Z…) vont au mode Modifier.
public sealed class EditBanner : Window
{
    private readonly Button _undo = new() { Content = "↶", ToolTip = "Annuler (Ctrl+Z)", MinWidth = 36, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _redo = new() { Content = "↷", ToolTip = "Rétablir (Ctrl+Y)", MinWidth = 36, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };

    public EditBanner(Action undo, Action redo, Action finish, Action<KeyEventArgs> keys)
    {
        Style = new Style(typeof(Window));
        MinWidth = MinHeight = 0;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Mode Modifier · OrbitWeave";
        Left = Top = -10000;

        _undo.Click += (_, _) => undo();
        _redo.Click += (_, _) => redo();
        var done = new Button { Content = "Terminer", MinWidth = 84, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE3)), Foreground = Brushes.Black };
        done.Click += (_, _) => finish();
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = "Mode Modifier · Échap pour finir", Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_undo);
        row.Children.Add(_redo);
        row.Children.Add(done);
        Content = new Border
        {
            Child = row, Height = 44, Padding = new Thickness(12, 4, 8, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x24, 0x25, 0x29)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5))
        };
        PreviewKeyDown += (_, e) => keys(e);
    }

    public void SetCommands(bool canUndo, bool canRedo) { _undo.IsEnabled = canUndo; _redo.IsEnabled = canRedo; }

    // En haut au milieu de la zone de travail de l'écran où se trouve la roue.
    public void ShowOn(Window wheel)
    {
        if (Owner is null && wheel.IsVisible) Owner = wheel;
        if (!IsVisible) Show();
        UpdateLayout();
        var handle = new System.Windows.Interop.WindowInteropHelper(wheel).Handle;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
        var bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
        Left = Math.Round((topLeft.X + bottomRight.X - ActualWidth) / 2);
        Top = topLeft.Y + 10;
    }
}
