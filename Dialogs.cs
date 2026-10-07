using System.Windows;
using Wpf.Ui.Controls;
using FluentMessageBox = Wpf.Ui.Controls.MessageBox;
using FluentMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace OrbitWeave;

// Boîtes de dialogue au style Fluent, à la place des MessageBox Win32.
internal static class Dialogs
{
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string confirm)
    {
        var box = Create(owner, title, message);
        box.PrimaryButtonText = confirm;
        box.PrimaryButtonAppearance = ControlAppearance.Danger;
        box.CloseButtonText = "Annuler";
        return await box.ShowDialogAsync() == FluentMessageBoxResult.Primary;
    }

    public static async Task InfoAsync(Window? owner, string title, string message)
    {
        var box = Create(owner, title, message);
        box.CloseButtonText = "OK";
        box.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; box.TemplateButtonCommand.Execute(Wpf.Ui.Controls.MessageBoxButton.Close); } };
        await box.ShowDialogAsync();
    }

    // Saisie d'un texte court (nom de profil) ; null si annulé ou vide.
    public static async Task<string?> PromptAsync(Window? owner, string title, string message, string initial, string confirm)
    {
        var box = Create(owner, title, message);
        var input = new System.Windows.Controls.TextBox { Text = initial, Margin = new Thickness(0, 12, 0, 0), MaxLength = 40 };
        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
        panel.Children.Add(input);
        box.Content = panel;
        box.PrimaryButtonText = confirm;
        box.CloseButtonText = "Annuler";
        box.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; box.TemplateButtonCommand.Execute(Wpf.Ui.Controls.MessageBoxButton.Primary); } };
        var result = await box.ShowDialogAsync();
        return result == FluentMessageBoxResult.Primary && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }

    private static FluentMessageBox Create(Window? owner, string title, string message)
    {
        var box = new FluentMessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 }
        };
        Ui.UiAutoIds.Assign(box, "dialog");
        box.Loaded += (_, _) =>
        {
            if (Ui.UiRuntime.Profile.CustomElements.Values.Any(item => item.Parent == "dialog") &&
                box.Content is UIElement original)
            {
                var layer = new System.Windows.Controls.Grid();
                layer.Children.Add(original);
                var overlay = new System.Windows.Controls.Canvas();
                layer.Children.Add(overlay);
                box.Content = layer;
                Ui.UiCustomElements.AddTo(overlay, Ui.UiRuntime.Profile, "dialog");
            }
            Ui.UiAutoIds.Assign(box, "dialog");
            Ui.UiOverrides.ApplyTree(box, Ui.UiRuntime.Profile);
        };
        box.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; box.TemplateButtonCommand.Execute(Wpf.Ui.Controls.MessageBoxButton.Close); } };
        // Le widget vit derrière les fenêtres : sans propriétaire visible, la boîte se place
        // au centre de l'écran et au premier plan.
        if (owner is { IsVisible: true } && owner is not MainWindow)
        {
            box.Owner = owner;
            box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // La roue en mode édition est au premier plan : la boîte doit passer devant elle.
            box.Topmost = System.Windows.Application.Current.Windows.OfType<Window>().Any(window => window.Topmost && window.IsVisible);
        }
        else
        {
            box.Owner = null;
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            box.Topmost = true;
            // Un clic sur la roue laisse le Bureau au premier plan : on prend le focus pour Entrée et Échap.
            box.ContentRendered += (_, _) => box.Activate();
        }
        return box;
    }
}
