namespace VsLike.Debugging;

/// <param name="VariablesReference">Non-zero when the value has children (objects, collections).</param>
public sealed record VariableInfo(string Name, string Value, string? Type, int VariablesReference);
