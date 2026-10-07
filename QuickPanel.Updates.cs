using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbitWeave.Updates;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace OrbitWeave;

// « Mise à jour » (en bas de la fenêtre agrandie) : version actuelle, et « Mettre à jour » quand GitHub en a une plus récente.
public sealed partial class QuickPanel
{
    private readonly TextBlock _updateText = Text("", 11, FontWeights.Normal, new Thickness(0, 6, 0, 0), secondary: true);
    private readonly Button _updateButton = new()
    {
        Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
        Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1)
    };

    private void BuildUpdates()
    {
        _more.Children.Add(Text("Mise à jour", 12, FontWeights.SemiBold, new Thickness(0, 14, 0, 0), secondary: true));
        _updateText.TextWrapping = TextWrapping.Wrap;
        _updateText.TextTrimming = TextTrimming.None;
        _more.Children.Add(_updateText);
        _updateButton.Template = TileTemplate();
        _updateButton.Click += async (_, _) =>
        {
            if (Updater.State == UpdateState.Available && Updater.CanInstall)
            {
                if (await Updater.InstallAsync() is { } problem) _updateText.Text = problem;
            }
            else await Updater.CheckAsync();
        };
        _more.Children.Add(_updateButton);
        Updater.Changed += () => Dispatcher.BeginInvoke(ShowUpdate);
        // Fenêtre rouverte longtemps après la dernière vérification : on regarde de nouveau.
        _refreshers.Add(settings =>
        {
            ShowUpdate();
            if (Updater.State is UpdateState.UpToDate or UpdateState.Failed && DateTime.Now - Updater.LastCheck > TimeSpan.FromMinutes(30))
                _ = Updater.CheckAsync();
        });
        ShowUpdate();
    }

    private void ShowUpdate()
    {
        var current = ReleaseInfo.Display(Updater.Current);
        var latest = Updater.Latest is { } release ? ReleaseInfo.Display(release.Version) : null;
        var (text, button) = Updater.State switch
        {
            UpdateState.Checking => ($"Version {current} · recherche d'une mise à jour…", null),
            UpdateState.UpToDate => ($"Version {current} · à jour.", "Vérifier maintenant"),
            UpdateState.Available when Updater.CanInstall => ($"Version {current} · la version {latest} est disponible.", $"Mettre à jour vers {latest}"),
            UpdateState.Available => ($"Version {current} · la version {latest} est disponible. Cette copie est compilée depuis le code : " +
                "installe OrbitWeave avec son installateur pour les mises à jour en un clic.", null),
            UpdateState.Downloading => ($"Téléchargement de la version {latest}… OrbitWeave va se fermer puis se relancer.", null),
            UpdateState.Failed => ($"Version {current} · impossible de vérifier les mises à jour (pas de connexion ?).", "Réessayer"),
            _ => ($"Version {current}", "Vérifier maintenant")
        };
        _updateText.Text = text;
        _updateButton.Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
        if (button is not null) _updateButton.Content = Centered(button);
        var highlight = Updater.State == UpdateState.Available;
        _updateButton.Background = highlight ? new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x3E, 0xA5)) : new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        _updateButton.BorderBrush = highlight ? new SolidColorBrush(Color.FromRgb(0xFF, 0x3E, 0xA5)) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        if (!_expanded) _moreLink.Text = highlight ? "Plus de réglages… · mise à jour disponible" : "Plus de réglages…";
    }
}
