public class Box<T> : Base, IBox
    where T : struct
{
}

public record P(int X) : Shape;
