using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

// Mouse, wheel, pinch and keyboard handling of the canvas (the gestures of DiagramEditorView.swift and ScrollMonitor).
internal sealed partial class DiagramCanvas
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var box = HitBox(point);
        if (e.ClickCount == 2 && box is not null)
        {
            // Double click: open the type, or the member under the pointer, in the editor.
            var index = box.MemberIndex(DiagramPoint(point));
            var member = index is { } i ? box.Type.Members[i] : null;
            _ = _model.OpenInEditor(box.Type, member);
            _pressPoint = null;
            e.Handled = true;
            return;
        }

        _pressPoint = point;
        _pressBox = box;
        _isDragging = false;
        _panStart = Offset;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressPoint is not { } start)
        {
            return;
        }

        var point = e.GetPosition(this);
        var translation = point - start;
        if (!_isDragging && Math.Abs(translation.X) < DragThreshold && Math.Abs(translation.Y) < DragThreshold)
        {
            return;
        }

        _isDragging = true;
        if (_pressBox is { } box)
        {
            if (_model.Tool == DiagramTool.Inherit)
            {
                _connection = (box.Type.FullName, point);
            }
            else
            {
                _dragOffsets[box.Type.FullName] = translation;
            }
        }
        else
        {
            Offset = _panStart + translation;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressPoint is not { } start)
        {
            return;
        }

        var point = e.GetPosition(this);
        var box = _pressBox;
        _pressPoint = null;
        _pressBox = null;
        e.Pointer.Capture(null);
        if (_isDragging)
        {
            EndDrag(box, point - start);
        }
        else if (box is not null)
        {
            Tap(box, point);
        }
        else
        {
            BackgroundTapped(point);
        }

        _isDragging = false;
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isDragging)
        {
            _dragOffsets.Clear();
            _connection = null;
            _pressPoint = null;
            _isDragging = false;
            InvalidateVisual();
        }
    }

    private void EndDrag(DiagramBox? box, Vector translation)
    {
        if (box is null)
        {
            PersistView();
            return;
        }

        if (_model.Tool == DiagramTool.Inherit)
        {
            if (_connection is { } connection)
            {
                _connection = null;
                var target = DiagramPoint(connection.To);
                var destination = _model.Boxes.FirstOrDefault(b => b.Frame.Contains(target) && b.Type.FullName != box.Type.FullName);
                if (destination is not null)
                {
                    _ = _model.Connect(box.Type.FullName, destination.Type.FullName);
                }
            }

            _model.Tool = DiagramTool.Select;
            return;
        }

        _dragOffsets.Remove(box.Type.FullName);
        _model.Move(box.Type.FullName, new CGPoint(box.Frame.MinX + (translation.X / Zoom), box.Frame.MinY + (translation.Y / Zoom)));
    }

    /// <summary>A click on a box: the collapse chevron toggles, anything else selects.</summary>
    private void Tap(DiagramBox box, Point point)
    {
        var target = DiagramPoint(point);
        if (_rendering?.CollapseButtonFrame(box) is { } chevron && chevron.Inflate(4).Contains(new Point(target.X, target.Y)))
        {
            _model.ToggleCollapsed(box.Type.FullName);
            return;
        }

        _model.Selection = box.Type.FullName;
    }

    private void BackgroundTapped(Point point)
    {
        var tool = _model.Tool;
        if (tool.Kind is { } kind)
        {
            _model.Tool = DiagramTool.Select;
            CreateRequested?.Invoke(this, (kind, DiagramPoint(point)));
        }
        else if (tool == DiagramTool.Inherit)
        {
            _model.Tool = DiagramTool.Select;
        }
        else
        {
            _model.Selection = null;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var point = e.GetPosition(this);
        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta))
        {
            // Ctrl + wheel zooms around the pointer (⌘ + scroll on macOS: 1 % per point, 10 points per line).
            ZoomBy(1 + (e.Delta.Y * 0.1), point);
        }
        else
        {
            var delta = e.Delta;
            if (modifiers.HasFlag(KeyModifiers.Shift) && delta.X == 0)
            {
                delta = new Vector(delta.Y, 0);
            }

            Offset += delta * WheelPanStep;
            PersistView();
        }

        e.Handled = true;
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        var factor = e.Scale / _lastPinchScale;
        _lastPinchScale = e.Scale;
        ZoomBy(factor, e.ScaleOrigin);
        e.Handled = true;
    }

    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        ZoomBy(1 + e.Delta.X, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && _model.SelectedType is { } type)
        {
            _ = _model.DeleteType(type);
            e.Handled = true;
        }
    }

    private void OnContextMenuRequested(object? sender, ContextRequestedEventArgs e)
    {
        var point = e.TryGetPosition(this, out var position) ? position : new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (HitBox(point) is { } box)
        {
            BoxMenuRequested?.Invoke(this, box);
        }
        else
        {
            CanvasMenuRequested?.Invoke(this, DiagramPoint(point));
        }

        e.Handled = true;
    }
}
