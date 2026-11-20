using Lib2;

namespace Lib1;

public class Car
{
    private readonly Engine _engine = new();

    public string Name { get; set; } = "Car";

    public string Drive() => $"{Name}: {_engine.Start()}";
}
