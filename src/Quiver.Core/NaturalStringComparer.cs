namespace Quiver.Core;

/// <summary>
/// Orders names the way Explorer does: numbers by value ("Verbale 2" before "Verbale 10"), letters ignoring case.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var numberX = x.AsSpan(startX, i - startX).TrimStart('0');
                var numberY = y.AsSpan(startY, j - startY).TrimStart('0');
                if (numberX.Length != numberY.Length) return numberX.Length.CompareTo(numberY.Length);
                var byDigits = numberX.CompareTo(numberY, StringComparison.Ordinal);
                if (byDigits != 0) return byDigits;
            }
            else
            {
                var byChar = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (byChar != 0) return byChar;
                i++;
                j++;
            }
        }

        var byRemainingLength = (x.Length - i).CompareTo(y.Length - j);
        // Names equal for the comparison (like "a01" and "a1") still need a stable order.
        return byRemainingLength != 0 ? byRemainingLength : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
