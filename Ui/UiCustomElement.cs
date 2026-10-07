namespace OrbitWeave.Ui;

// Élément créé par l'utilisateur et enregistré dans son profil d'interface.
public sealed class UiCustomElement
{
    public string Id { get; set; } = "";
    public string Parent { get; set; } = "wheel";
    public string Kind { get; set; } = "Text";
    public string Content { get; set; } = "Texte";
    public string Group { get; set; } = "";
    public string AttachTo { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 32;
}
