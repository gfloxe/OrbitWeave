using System.Text.Json.Serialization;

namespace OrbitWeave.Actions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(OpenStep), "Ouvrir")]
[JsonDerivedType(typeof(ScriptStep), "Script")]
[JsonDerivedType(typeof(WaitStep), "Attendre")]
public abstract class ActionStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
}

public sealed class OpenStep : ActionStep
{
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public bool ReuseExisting { get; set; } = true;
}

public enum ScriptSource { Fichier, Commande }
public enum ScriptShell { Auto, PowerShell, Cmd }

public sealed class ScriptStep : ActionStep
{
    public ScriptSource Source { get; set; } = ScriptSource.Fichier;
    public string Path { get; set; } = "";
    public string Code { get; set; } = "";
    public ScriptShell Shell { get; set; } = ScriptShell.Auto;
    public bool Hidden { get; set; }
    public bool Admin { get; set; }
    public bool WaitForExit { get; set; }
}

public sealed class WaitStep : ActionStep
{
    private double _seconds = 1;
    public double Seconds { get => _seconds; set => _seconds = Math.Clamp(value, 0.1, 3600); }
}
