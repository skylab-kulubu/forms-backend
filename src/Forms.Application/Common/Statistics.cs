namespace Skylab.Forms.Application.Common;

public static class Statistics
{
    public static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();

        if (sorted.Count == 0) return null;

        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}
