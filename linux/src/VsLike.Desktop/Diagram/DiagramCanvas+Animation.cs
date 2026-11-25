using Avalonia;
using Avalonia.Threading;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

// The animations SwiftUI gives the diagram with `withAnimation(.snappy)`: fitting the view and arranging the boxes.
internal sealed partial class DiagramCanvas
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(280);

    private DispatcherTimer? _viewAnimation;
    private DispatcherTimer? _layoutAnimation;
    private Dictionary<string, CGRect>? _layoutFrom;
    private DateTime _layoutStart;
    private bool _animateNextLayout;
    private IReadOnlyList<DiagramBox> _previousBoxes = [];

    /// <summary>Moves the boxes smoothly to their next positions (used before "Arrange automatically").</summary>
    public void AnimateNextLayoutChange() => _animateNextLayout = true;

    private void AnimateView(double zoom, Vector offset)
    {
        _viewAnimation?.Stop();
        var startZoom = Zoom;
        var startOffset = Offset;
        var started = DateTime.UtcNow;
        _viewAnimation = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var t = Progress(started);
            Zoom = startZoom + ((zoom - startZoom) * t);
            Offset = startOffset + ((offset - startOffset) * t);
            OnViewChanged();
            if (t >= 1)
            {
                _viewAnimation?.Stop();
            }
        });
        _viewAnimation.Start();
    }

    private void OnBoxesChanged()
    {
        var previous = _previousBoxes;
        _previousBoxes = _model.Boxes;
        if (!_animateNextLayout)
        {
            return;
        }

        _animateNextLayout = false;
        _layoutFrom = previous.GroupBy(b => b.Type.FullName).ToDictionary(g => g.Key, g => g.First().Frame);
        _layoutStart = DateTime.UtcNow;
        _layoutAnimation?.Stop();
        _layoutAnimation = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            if (Progress(_layoutStart) >= 1)
            {
                _layoutFrom = null;
                _layoutAnimation?.Stop();
            }

            InvalidateVisual();
        });
        _layoutAnimation.Start();
    }

    /// <summary>Boxes between their old and new positions while the arrange animation runs, otherwise null.</summary>
    private IReadOnlyList<DiagramBox>? AnimatedBoxes()
    {
        if (_layoutFrom is not { } from)
        {
            return null;
        }

        var t = Progress(_layoutStart);
        return [.. _model.Boxes.Select(box => from.TryGetValue(box.Type.FullName, out var old) ? Interpolated(box, old, t) : box)];
    }

    private static DiagramBox Interpolated(DiagramBox box, CGRect old, double t) =>
        MovedTo(box, old.X + ((box.Frame.X - old.X) * t), old.Y + ((box.Frame.Y - old.Y) * t));

    /// <summary>Ease-out progress from 0 to 1 over the animation duration (close to SwiftUI's snappy spring).</summary>
    private static double Progress(DateTime started)
    {
        var linear = Math.Min(1, (DateTime.UtcNow - started) / AnimationDuration);
        return 1 - Math.Pow(1 - linear, 3);
    }
}
