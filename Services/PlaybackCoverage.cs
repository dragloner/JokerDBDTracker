namespace JokerDBDTracker.Services;

public sealed class PlaybackCoverage
{
    private readonly List<(double Start, double End)> _ranges = [];
    public double Seconds => _ranges.Sum(range => range.End - range.Start);
    public void Add(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start) return;
        _ranges.Add((start, end));
        _ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (var i = 0; i + 1 < _ranges.Count;)
        {
            var current = _ranges[i];
            var next = _ranges[i + 1];
            if (next.Start <= current.End)
            {
                _ranges[i] = (current.Start, Math.Max(current.End, next.End));
                _ranges.RemoveAt(i + 1);
            }
            else i++;
        }
    }
}
