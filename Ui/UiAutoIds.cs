using System.Windows;

namespace OrbitWeave.Ui;

// Identifiants stables calculés depuis l'arbre logique, pour les fenêtres décrites en XAML :
// id du parent + « / » + x:Name, sinon Type#rang parmi les frères du même type sans identifiant explicite.
public static class UiAutoIds
{
    private static readonly DependencyProperty AutoProperty = DependencyProperty.RegisterAttached(
        "Auto", typeof(bool), typeof(UiAutoIds), new PropertyMetadata(false));

    public static void Assign(DependencyObject root, string rootId)
    {
        if (UiId.Get(root) is null || (bool)root.GetValue(AutoProperty))
        {
            UiId.Set(root, rootId);
            root.SetValue(AutoProperty, true);
        }
        Visit(root);
    }

    private static void Visit(DependencyObject parent)
    {
        var parentId = UiId.Get(parent)!;
        var ranks = new Dictionary<string, int>();
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<FrameworkElement>())
        {
            if (UiId.Get(child) is null || (bool)child.GetValue(AutoProperty))
            {
                string segment;
                if (!string.IsNullOrEmpty(child.Name)) segment = child.Name;
                else
                {
                    var type = child.GetType().Name;
                    ranks[type] = ranks.GetValueOrDefault(type) + 1;
                    segment = $"{type}#{ranks[type]}";
                }
                UiId.Set(child, $"{parentId}/{segment}");
                child.SetValue(AutoProperty, true);
            }
            Visit(child);
        }
    }

    public static IReadOnlyList<string> Collect(DependencyObject root)
    {
        var ids = new List<string>();
        void Walk(DependencyObject node)
        {
            if (UiId.Get(node) is { } id) ids.Add(id);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(root);
        return ids;
    }
}
