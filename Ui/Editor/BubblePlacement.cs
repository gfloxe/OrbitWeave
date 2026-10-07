using System.Windows;
using System.Windows.Media;
using Size = System.Windows.Size;

namespace OrbitWeave.Ui.Editor;

// Où poser la bulle d'un bouton : du côté où il y a de la place, toujours dans la zone de travail.
public static class BubblePlacement
{
    // avoid : une fenêtre à ne pas recouvrir (l'éditeur en mode Modifier) ; un côté qui la chevauche passe après l'autre.
    public static Point Place(Rect target, Size bubble, Rect work, double gap = 12, Rect? avoid = null)
    {
        var y = Math.Clamp(target.Top, work.Top, Math.Max(work.Top, work.Bottom - bubble.Height));
        var right = target.Right + gap;
        var left = target.Left - gap - bubble.Width;
        bool Fits(double x) => x >= work.Left && x + bubble.Width <= work.Right;
        bool Free(double x) => avoid is not { } zone || !zone.IntersectsWith(new Rect(x, y, bubble.Width, bubble.Height));
        foreach (var x in new[] { right, left }) if (Fits(x) && Free(x)) return new Point(x, y);
        if (Fits(right)) return new Point(right, y);
        if (Fits(left)) return new Point(left, y);
        var fallback = work.Right - target.Right >= target.Left - work.Left ? work.Right - bubble.Width : work.Left;
        return new Point(Math.Max(work.Left, fallback), y);
    }

    // Pose une bulle déjà affichée à côté de sa cible (pixels d'écran), dans la zone de travail de l'écran de la cible.
    public static void MoveNextTo(Window bubble, Rect targetScreenPixels, Rect? avoid)
    {
        bubble.UpdateLayout();
        var fromDevice = PresentationSource.FromVisual(bubble)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var center = new System.Drawing.Point((int)targetScreenPixels.X + (int)(targetScreenPixels.Width / 2),
            (int)targetScreenPixels.Y + (int)(targetScreenPixels.Height / 2));
        var area = System.Windows.Forms.Screen.FromPoint(center).WorkingArea;
        var work = new Rect(fromDevice.Transform(new Point(area.Left, area.Top)), fromDevice.Transform(new Point(area.Right, area.Bottom)));
        var target = new Rect(fromDevice.Transform(targetScreenPixels.TopLeft), fromDevice.Transform(targetScreenPixels.BottomRight));
        var place = Place(target, new Size(bubble.ActualWidth, bubble.ActualHeight), work, gap: 2, avoid);
        bubble.Left = place.X;
        bubble.Top = place.Y;
    }
}
