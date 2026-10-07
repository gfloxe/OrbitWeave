namespace OrbitWeave.Orbit;

// Deux appuis du bouton gauche assez proches dans le temps et sur place font un double-clic,
// avec les seuils de Windows (délai du double-clic, petit rectangle autour du premier appui).
public sealed class DoubleClickDetector(uint maxDelayMs, int maxDx, int maxDy)
{
    private uint _lastTime;
    private int _lastX, _lastY;
    private bool _armed;

    public bool Press(uint timeMs, int x, int y)
    {
        var isDouble = _armed && unchecked(timeMs - _lastTime) <= maxDelayMs &&
            Math.Abs(x - _lastX) <= maxDx / 2 && Math.Abs(y - _lastY) <= maxDy / 2;
        // Le deuxième appui d'un double-clic ne commence pas le suivant : trois appuis ne font qu'un double-clic.
        _armed = !isDouble;
        (_lastTime, _lastX, _lastY) = (timeMs, x, y);
        return isDouble;
    }
}
