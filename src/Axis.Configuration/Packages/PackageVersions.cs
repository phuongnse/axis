namespace Axis.Configuration.Packages;

/// <summary>Orders <c>major.minor.patch</c> package versions.</summary>
internal static class PackageVersions
{
    /// <summary>Compares two <c>major.minor.patch</c> versions without leading zeros, part by part.</summary>
    public static int Compare(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < leftParts.Length; index++)
        {
            var byLength = leftParts[index].Length.CompareTo(rightParts[index].Length);
            var byPart = byLength != 0 ? byLength : string.CompareOrdinal(leftParts[index], rightParts[index]);
            if (byPart != 0)
            {
                return byPart;
            }
        }

        return 0;
    }
}
