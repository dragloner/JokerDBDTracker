using System.Windows;
using System.Windows.Media;

namespace JokerDBDTracker.Views;

/// <summary>One drawing surface for particles, atmospheric light and the entrance pulse.</summary>
public sealed class PsiAtmosphere : FrameworkElement
{
    private const int ParticleCount = 54;
    private static readonly Brush Green = Frozen(new SolidColorBrush(Color.FromArgb(115, 186, 215, 126)));
    private static readonly Brush Amber = Frozen(new SolidColorBrush(Color.FromArgb(105, 230, 190, 115)));
    private static readonly Brush Haze = Frozen(new RadialGradientBrush(Color.FromArgb(35, 150, 182, 76), Colors.Transparent));
    private static readonly Pen Ring = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(45, 177, 206, 112)), 1));
    private static readonly Pen Pulse = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(100, 214, 234, 166)), 2));
    public double Time { get; set; }
    public double ExitProgress { get; set; }
    public bool Animate { get; set; }

    private static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }

    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var time = Animate ? Time : 0;
        drawing.DrawEllipse(Haze, null, new Point(width * .58 + Math.Sin(time * .13) * 30, height * .63), width * .34, height * .3);
        var center = new Point(width * .73, height * .48);
        for (var ring = 0; ring < 3; ring++)
        {
            var radius = 120 + ring * 36 + Math.Sin(time * .4 + ring) * 5;
            drawing.PushOpacity(.25 + .1 * Math.Sin(time * .3 + ring));
            drawing.DrawEllipse(null, Ring, center, radius, radius);
            drawing.Pop();
        }
        for (var index = 0; index < ParticleCount; index++)
        {
            var seed = index * 73.137;
            var x = (Fraction(seed) * width + Math.Sin(time * .14 + seed) * 18 + width) % width;
            var y = (Fraction(seed * .713) * height - time * (3 + index % 6) % height + height * 2) % height;
            drawing.PushOpacity(.25 + .45 * Math.Pow(Math.Sin(time * .35 + seed), 2));
            drawing.DrawEllipse(index % 7 == 0 ? Amber : Green, null, new Point(x, y), index % 3 == 0 ? 1.7 : .8, index % 3 == 0 ? 1.7 : .8);
            drawing.Pop();
        }
        // Thin interference bands travel slowly; opacity stays low to avoid flashing.
        if (Animate)
        {
            var bandY = time * 12 % (height + 30) - 15;
            drawing.PushOpacity(.18);
            drawing.DrawRectangle(Green, null, new Rect(0, bandY, width, 1));
            drawing.DrawRectangle(Green, null, new Rect(0, bandY + 4, width, .5));
            drawing.Pop();
        }
        if (ExitProgress > 0 && ExitProgress < 1)
        {
            drawing.PushOpacity(1 - ExitProgress);
            var radius = Math.Sqrt(width * width + height * height) * ExitProgress;
            drawing.DrawEllipse(null, Pulse, new Point(width * .26, height * .64), radius, radius);
            drawing.Pop();
        }
    }

    private static double Fraction(double value) => value - Math.Floor(value);
}
