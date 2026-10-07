using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Application = System.Windows.Application;

namespace OrbitWeave;

// Petite notification en bas à droite de l'écran principal : elle ne prend pas le focus et ne bloque
// aucune fenêtre (une boîte modale désactivait la roue tant qu'elle restait ouverte, parfois cachée).
internal static class Notifications
{
    private static readonly List<Window> Open = [];
    private const double Gap = 12;

    // actionLabel/action : un bouton (ex. « Annuler ») qui agit puis ferme la notification.
    public static void Show(string title, string message, string? actionLabel = null, Action? action = null)
    {
        var text = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13,
            Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = message, FontSize = 12, Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), TextWrapping = TextWrapping.Wrap });
        var card = new Border
        {
            Child = text, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x26, 0x27, 0x29)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            ToolTip = "Clic pour fermer"
        };
        var window = new Window
        {
            // Style vide : le style implicite de la bibliothèque d'interface impose une taille minimale.
            Style = new Style(typeof(Window)), MinWidth = 0, MinHeight = 0,
            Title = "OrbitWeave", Content = card, Width = 340, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Opacity = 0,
            Left = -10000, Top = -10000
        };
        window.MouseLeftButtonUp += (_, _) => Close(window);
        if (actionLabel is not null && action is not null)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = actionLabel, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12, 3, 12, 3), Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0)
            };
            var done = false;
            button.Click += (_, e) =>
            {
                e.Handled = true;
                if (done) return;
                done = true;
                action();
                Close(window);
            };
            text.Children.Add(button);
            card.ToolTip = null;
        }
        // Placée une fois sa hauteur connue, puis révélée.
        window.ContentRendered += (_, _) =>
        {
            Open.Add(window);
            Arrange();
            window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(action is null ? 7 : 10) };
        timer.Tick += (_, _) => { timer.Stop(); Close(window); };
        window.Closed += (_, _) => { timer.Stop(); Open.Remove(window); Arrange(); };
        window.Show();
        timer.Start();
    }

    private static void Close(Window window)
    {
        var fade = new DoubleAnimation(window.Opacity, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => window.Close();
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // Empilées du bas vers le haut, dans la zone de travail de l'écran principal (au-dessus de la barre des tâches).
    private static void Arrange()
    {
        var work = SystemParameters.WorkArea;
        var bottom = work.Bottom - Gap;
        for (var i = Open.Count - 1; i >= 0; i--)
        {
            var window = Open[i];
            window.Left = work.Right - window.Width - Gap;
            window.Top = bottom - window.ActualHeight;
            bottom = window.Top - 8;
        }
    }
}
