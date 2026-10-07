namespace OrbitWeave.Ui.Editor;

public static class WheelEditingRules
{
    public static bool DrawSelection(bool editing, string? selection) => editing && selection is not null;
}
