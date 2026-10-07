using System.Windows;

namespace OrbitWeave.Ui;

// Identifiant stable d'un élément d'interface, clé de ses surcharges dans le profil.
// « modèle:instance » : les surcharges du modèle valent pour toutes ses instances.
public static class UiId
{
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(UiId), new PropertyMetadata(null));
    public static string? Get(DependencyObject element) => (string?)element.GetValue(IdProperty);
    public static void Set(DependencyObject element, string? value) => element.SetValue(IdProperty, value);

    // Un élément protégé ne peut être ni masqué ni rendu transparent : on garde toujours un chemin vers les réglages.
    public static readonly DependencyProperty ProtectedProperty = DependencyProperty.RegisterAttached(
        "Protected", typeof(bool), typeof(UiId), new PropertyMetadata(false));
    public static bool IsProtected(DependencyObject element) => (bool)element.GetValue(ProtectedProperty);
    public static void SetProtected(DependencyObject element, bool value) => element.SetValue(ProtectedProperty, value);

    public static T With<T>(this T element, string id, bool isProtected = false) where T : DependencyObject
    {
        Set(element, id);
        if (isProtected) SetProtected(element, true);
        return element;
    }
}
