namespace OrbitWeave.Orbit;

public readonly record struct BranchBox(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public double CenterY => Top + Height / 2;
    public bool Intersects(BranchBox other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
}

// FirstY : centre vertical de la première carte.
public readonly record struct SubBranchPlan(double Left, double FirstY, int Side);

// Une sous-branche part du bord de sa carte parente, du côté qui ne sort pas de l'écran et ne recouvre rien.
public static class SubBranchLayout
{
    public static SubBranchPlan Place(BranchBox parent, int count, double width, double height,
        double pitch, double gap, int preferredSide, BranchBox bounds, IReadOnlyList<BranchBox> occupied)
    {
        var side = preferredSide < 0 ? -1 : 1;
        var span = (count - 1) * pitch;
        var firstY = parent.CenterY - span / 2;
        double LeftFor(int s) => s > 0 ? parent.Right + gap : parent.Left - gap - width;
        double Overflow(int s) => Math.Max(0, bounds.Left - LeftFor(s)) + Math.Max(0, LeftFor(s) + width - bounds.Right);
        bool Fits(int s)
        {
            var column = new BranchBox(LeftFor(s), firstY - height / 2, width, span + height);
            return Overflow(s) == 0 && !occupied.Any(column.Intersects);
        }
        var chosen = Fits(side) ? side : Fits(-side) ? -side : Overflow(side) <= Overflow(-side) ? side : -side;
        var top = Math.Clamp(firstY - height / 2, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - span - height));
        return new SubBranchPlan(LeftFor(chosen), top + height / 2, chosen);
    }
}
