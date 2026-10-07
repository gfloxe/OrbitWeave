using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitWeave.Ui;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using Path = System.IO.Path;
using Brush = System.Windows.Media.Brush;
using Icon = System.Drawing.Icon;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace OrbitWeave.Orbit;

internal static class OrbitCardFactory
{
    public static Button Create(ActionItem action, bool hasChildren = false)
    {
        var iconBox = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
            Background = (Brush)System.Windows.Application.Current.FindResource("CardIconBoxBrush"),
            VerticalAlignment = VerticalAlignment.Center
        }.With($"wheel.card.icon:{action.Id}");
        // Icône réelle de ce que le bouton ouvre : tout de suite si elle est déjà chargée, sinon le symbole
        // en attendant qu'elle arrive (chargée hors du fil de la roue). Symbole choisi ou illisible : le symbole reste.
        var target = Actions.IconTarget.For(action);
        if (AppIcons.TryGetLoaded(target, out var loaded) && loaded is not null) iconBox.Child = IconImage(loaded);
        else
        {
            iconBox.Child = new TextBlock { Text = action.Icon, FontSize = 16, Foreground = (Brush)System.Windows.Application.Current.FindResource("WidgetPrimaryTextBrush"),
                FontFamily = new FontFamily("Segoe UI Symbol"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (target.Kind != Actions.IconKind.None) SwapWhenLoaded(iconBox, target);
        }

        var title = new TextBlock { Text = action.Name, FontSize = (double)System.Windows.Application.Current.FindResource("CardTitleSize"), FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)System.Windows.Application.Current.FindResource("WidgetPrimaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis }.With($"wheel.card.title:{action.Id}");
        var subtitle = new TextBlock { Text = Subtitle(action), FontSize = (double)System.Windows.Application.Current.FindResource("CardSubtitleSize"),
            Foreground = (Brush)System.Windows.Application.Current.FindResource("WidgetSecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis }.With($"wheel.card.subtitle:{action.Id}");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(subtitle);

        var content = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(iconBox);
        Grid.SetColumn(text, 1);
        text.Margin = new Thickness(10, 0, 0, 0);
        content.Children.Add(text);
        if (hasChildren)
        {
            // Chevron : la carte ouvre sa propre branche de sous-cartes.
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chevron = new TextBlock { Text = "›", FontSize = 18, Margin = new Thickness(6, 0, 0, 2), VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)System.Windows.Application.Current.FindResource("WidgetSecondaryTextBrush") };
            Grid.SetColumn(chevron, 2);
            content.Children.Add(chevron);
        }

        return new Button { Content = content, Opacity = 0, ToolTip = null,
            Style = (Style)System.Windows.Application.Current.FindResource("OrbitCard") }.With($"wheel.card:{action.Id}");
    }

    private static string Subtitle(ActionItem action)
    {
        if (Uri.TryCreate(action.Target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (Path.IsPathRooted(action.Target))
            return Path.GetFileName(action.Target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return action.Target;
    }

    public static Image IconImage(ImageSource source)
    {
        var image = new Image { Source = source, Width = 22, Height = 22, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static async void SwapWhenLoaded(Border iconBox, Actions.IconTarget target)
    {
        var image = await AppIcons.LoadAsync(target);
        if (image is not null) iconBox.Child = IconImage(image);
    }
}
