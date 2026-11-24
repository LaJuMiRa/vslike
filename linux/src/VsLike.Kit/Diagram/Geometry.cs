namespace VsLike.Kit;

/* Minimal stand-ins for CoreGraphics CGPoint/CGSize/CGRect, used by the diagram geometry (no UI dependency). */

public readonly record struct CGPoint(double X, double Y)
{
    public static readonly CGPoint Zero = new(0, 0);
}

public readonly record struct CGSize(double Width, double Height);

public readonly record struct CGRect(double X, double Y, double Width, double Height)
{
    public static readonly CGRect Zero = new(0, 0, 0, 0);

    public double MinX => X;

    public double MinY => Y;

    public double MaxX => X + Width;

    public double MaxY => Y + Height;

    public double MidX => X + (Width / 2);

    public double MidY => Y + (Height / 2);

    public CGRect Union(CGRect other)
    {
        var minX = Math.Min(MinX, other.MinX);
        var minY = Math.Min(MinY, other.MinY);
        return new CGRect(minX, minY, Math.Max(MaxX, other.MaxX) - minX, Math.Max(MaxY, other.MaxY) - minY);
    }

    public CGRect InsetBy(double dx, double dy) => new(X + dx, Y + dy, Width - (2 * dx), Height - (2 * dy));

    public bool Contains(CGPoint point) => point.X >= MinX && point.X < MaxX && point.Y >= MinY && point.Y < MaxY;

    public bool Intersects(CGRect other) => MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;
}
