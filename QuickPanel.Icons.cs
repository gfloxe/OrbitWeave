using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbitWeave.Actions;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;

namespace OrbitWeave;

// Ce dont « Mes icônes » a besoin : les boutons (pour savoir qui porte une image) et leur enregistrement.
public sealed record IconLibrary(Func<IReadOnlyList<ActionItem>> Actions, Func<List<ActionItem>, bool> Write, string DataDirectory);

// « Mes icônes » (fenêtre agrandie) : les images copiées dans les données, à voir, ajouter et supprimer.
// Une image s'applique à un bouton depuis sa bulle (clic sur son icône) ; la supprimer rend aux boutons leur icône automatique.
public sealed partial class QuickPanel
{
    private IconLibrary? _icons;
    private readonly WrapPanel _iconTiles = new() { Margin = new Thickness(-2, 6, -2, 0) };
    private readonly TextBlock _iconsHint = Text("", 11, FontWeights.Normal, new Thickness(0, 6, 0, 0), secondary: true);
    private string? _armedDelete;
    // Pendant une boîte de dialogue (choix de fichier), la fenêtre perd le focus sans devoir se fermer.
    private bool _inDialog;

    private void BuildIcons()
    {
        if (_icons is null) return;
        _more.Children.Add(Text("Mes icônes", 12, FontWeights.SemiBold, new Thickness(0, 14, 0, 0), secondary: true));
        _more.Children.Add(_iconTiles);
        _iconsHint.TextWrapping = TextWrapping.Wrap;
        _iconsHint.TextTrimming = TextTrimming.None;
        _more.Children.Add(_iconsHint);
        var add = new Button
        {
            Content = Centered("Ajouter des images…"), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Template = TileTemplate(),
            ToolTip = "PNG, JPG, ICO, BMP ou GIF ; copiées dans les données d'OrbitWeave"
        };
        add.Click += (_, _) => AddIcons();
        _more.Children.Add(add);
        _refreshers.Add(_ => RefreshIcons());
    }

    private void AddIcons()
    {
        if (_icons is not { } library) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Images à ajouter", Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif" };
        _inDialog = true;
        bool? chosen;
        try { chosen = dialog.ShowDialog(this); }
        finally { _inDialog = false; }
        Activate();
        if (chosen != true) return;
        var failed = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            try { CustomIcons.Import(file, library.DataDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { failed.Add(Path.GetFileName(file)); }
        }
        RefreshIcons();
        if (failed.Count > 0) ShowIconsHint("Impossible d'ajouter : " + string.Join(", ", failed));
    }

    private void RefreshIcons()
    {
        if (_icons is not { } library) return;
        _armedDelete = null;
        _iconTiles.Children.Clear();
        var files = CustomIcons.List(library.DataDirectory);
        var actions = library.Actions();
        foreach (var file in files) _iconTiles.Children.Add(IconTile(file, CustomIcons.UsedBy(actions, file)));
        ShowIconsHint(files.Count == 0
            ? "Aucune image pour l'instant. Une image ajoutée ici se choisit ensuite dans la bulle d'un bouton (clic sur son icône)."
            : "Clic sur ✕ pour supprimer. Pour mettre une image sur un bouton : sa bulle, clic sur son icône.");
    }

    private void ShowIconsHint(string text) => _iconsHint.Text = text;

    private FrameworkElement IconTile(string file, IReadOnlyList<string> users)
    {
        var image = new Image { Width = 36, Height = 36, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        _ = LoadInto(image, Path.Combine(_icons!.DataDirectory, file));
        var remove = new Button
        {
            Content = new TextBlock { Text = "✕", FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
            Width = 20, Height = 20, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -6, 0), Cursor = System.Windows.Input.Cursors.Hand, Template = TileTemplate(),
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x40, 0x40, 0x44)), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), ToolTip = "Supprimer cette image"
        };
        var grid = new Grid { Width = 52, Height = 52, Margin = new Thickness(2) };
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = image,
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            ToolTip = users.Count == 0 ? "Pas encore utilisée" : "Sur : " + string.Join(", ", users)
        };
        grid.Children.Add(frame);
        grid.Children.Add(remove);
        remove.Click += (_, _) =>
        {
            // Image portée par des boutons : un premier clic prévient, le second supprime.
            if (users.Count > 0 && _armedDelete != file)
            {
                _armedDelete = file;
                frame.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5));
                ShowIconsHint($"Utilisée par {string.Join(", ", users)}. Reclique sur ✕ pour la supprimer : ces boutons reprendront leur icône automatique.");
                return;
            }
            DeleteIcon(file);
        };
        return grid;
    }

    private void DeleteIcon(string file)
    {
        if (_icons is not { } library) return;
        try
        {
            var used = CustomIcons.UsedBy(library.Actions(), file).Count > 0;
            var updated = CustomIcons.Delete(library.Actions(), library.DataDirectory, file);
            if (used) library.Write(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowIconsHint("Impossible de supprimer : " + ex.Message);
            return;
        }
        RefreshIcons();
    }

    private static async Task LoadInto(Image image, string path) =>
        image.Source = await Orbit.AppIcons.LoadAsync(new IconTarget(IconKind.Image, path));
}
