namespace VsLike.App.Shell;

public static class LayoutAreas
{
    public const string Left = "left";
    public const string Right = "right";
    public const string Bottom = "bottom";
    public const string Documents = "documents";

    public static readonly IReadOnlyList<string> ToolAreas = [Left, Right, Bottom];

    public static readonly IReadOnlyDictionary<string, double> DefaultProportions = new Dictionary<string, double>
    {
        [Left] = 0.2,
        [Right] = 0.22,
        [Bottom] = 0.28,
    };
}
