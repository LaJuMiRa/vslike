namespace Lib2;

/// <summary>Drives a vehicle.</summary>
public class Engine
{
    public int Horsepower { get; init; } = 100;

    /// <summary>Starts the engine.</summary>
    /// <returns>A status message.</returns>
    public string Start() => $"Engine with {Horsepower} hp started";
}
