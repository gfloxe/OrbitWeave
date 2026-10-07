namespace OrbitWeave.Ui;

// Profil d'interface : jetons, jetons par thème, surcharges par identifiant d'élément.
// Les valeurs sont du texte invariant (« #AARRGGBB », « 12.5 », « true »).
public sealed class UiProfile
{
    public const int CurrentSchema = 6;
    public int Schema { get; set; } = CurrentSchema;
    public string Name { get; set; } = UiProfileStore.OriginName;
    public Dictionary<string, string> Tokens { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Themes { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Elements { get; set; } = new();
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> States { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Animations { get; set; } = new();
    public Dictionary<string, UiCustomElement> CustomElements { get; set; } = new();
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Resolutions { get; set; } = new();
    public Orbit.WheelLayoutData Layout { get; set; } = new();

    public UiProfile Clone(string name) => new()
    {
        Schema = CurrentSchema,
        Name = name,
        Layout = Layout.Clone(),
        Tokens = new(Tokens),
        Themes = Themes.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value)),
        Elements = Elements.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value)),
        States = States.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(
            state => state.Key, state => new Dictionary<string, string>(state.Value))),
        Animations = Animations.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value)),
        CustomElements = CustomElements.ToDictionary(pair => pair.Key, pair => new UiCustomElement
        {
            Id = pair.Value.Id, Parent = pair.Value.Parent, Kind = pair.Value.Kind,
            Content = pair.Value.Content, Group = pair.Value.Group, AttachTo = pair.Value.AttachTo, X = pair.Value.X, Y = pair.Value.Y,
            Width = pair.Value.Width, Height = pair.Value.Height
        }),
        Resolutions = Resolutions.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(
            element => element.Key, element => new Dictionary<string, string>(element.Value)))
    };
}
