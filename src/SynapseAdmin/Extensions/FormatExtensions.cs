namespace SynapseAdmin.Extensions;

public static class FormatExtensions
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };

    public static string FormatSize(this long byteCount)
    {
        if (byteCount == 0)
            return "0 B";

        var bytes = Math.Abs(byteCount);
        var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        var num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{(Math.Sign(byteCount) * num).ToString("0.#")} {SizeUnits[place]}";
    }
}
