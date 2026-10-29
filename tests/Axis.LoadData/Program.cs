using System.Globalization;
using Axis.LoadData;

const string Usage = "Usage: Axis.LoadData <folder> [--seed <n>]";

var seed = 1;
var valid = args.Length == 1 || (args.Length == 3 && args[1] == "--seed" && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed));
if (!valid || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var folder = args[0];
try
{
    LoadDataGenerator.Write(folder, seed);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

Console.WriteLine(Path.GetFullPath(folder));
return 0;
