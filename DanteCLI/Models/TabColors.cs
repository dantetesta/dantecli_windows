namespace DanteCLI.Models;

/// Mirrors macOS TabColor enum hex values for cross-OS settings portability.
public static class TabColors
{
    public const string Neutral = "#8E8E93";
    public const string Red     = "#FF453A";
    public const string Orange  = "#FF9F0A";
    public const string Yellow  = "#FFD60A";
    public const string Green   = "#30D158";
    public const string Mint    = "#63E6BE";
    public const string Cyan    = "#64D2FF";
    public const string Blue    = "#0A84FF";
    public const string Indigo  = "#5E5CE6";
    public const string Purple  = "#BF5AF2";
    public const string Pink    = "#FF375F";
    public const string Brown   = "#AC8E68";

    public static readonly (string Hex, string Label)[] All =
    {
        (Neutral, "Neutral"),
        (Red,     "Red"),
        (Orange,  "Orange"),
        (Yellow,  "Yellow"),
        (Green,   "Green"),
        (Mint,    "Mint"),
        (Cyan,    "Cyan"),
        (Blue,    "Blue"),
        (Indigo,  "Indigo"),
        (Purple,  "Purple"),
        (Pink,    "Pink"),
        (Brown,   "Brown"),
    };
}
