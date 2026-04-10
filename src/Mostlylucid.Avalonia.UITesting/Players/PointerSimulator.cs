using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Mostlylucid.Avalonia.UITesting.Players;

/// <summary>
/// Drives a real Avalonia window through the platform input pipeline by constructing
/// raw input events and dispatching them via Avalonia's IInputManager. This makes
/// synthesized pointer/touch/wheel/gesture events behave the same way real OS input
/// does — including hit-testing, IsPointerOver, capture, click counting, drag detection.
///
/// Reflection note: the constructors for RawPointerEventArgs, RawMouseWheelEventArgs,
/// RawTouchEventArgs, RawPointerGestureEventArgs, MouseDevice, Pointer, TouchDevice and
/// IInputManager.ProcessInput are public at runtime but hidden from Avalonia's
/// reference assemblies, so we bind them via reflection at construction time.
/// </summary>
public sealed class PointerSimulator
{
    // Raw pointer event types from Avalonia.Input.Raw.RawPointerEventType
    private const int Move = 11;
    private const int LeftButtonDown = 1;
    private const int LeftButtonUp = 2;
    private const int RightButtonDown = 3;
    private const int RightButtonUp = 4;
    private const int MiddleButtonDown = 5;
    private const int MiddleButtonUp = 6;
    private const int XButton1Down = 7;
    private const int XButton1Up = 8;
    private const int XButton2Down = 9;
    private const int XButton2Up = 10;
    private const int LeaveWindow = 0;
    private const int Wheel = 12;
    private const int TouchBegin = 14;
    private const int TouchUpdate = 15;
    private const int TouchEnd = 16;
    private const int TouchCancel = 17;
    private const int Magnify = 18;
    private const int Rotate = 19;
    private const int Swipe = 20;

    [Flags]
    private enum RawInputModifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Meta = 8,
        LeftMouseButton = 16,
        RightMouseButton = 32,
        MiddleMouseButton = 64,
        XButton1MouseButton = 128,
        XButton2MouseButton = 256,
    }

    private static int _nextPointerId = 1;

    // Reflection-bound types and constructors
    private static readonly Lazy<ReflectionBindings> Bindings = new(LoadBindings);

    private readonly object _pointer;
    private readonly object _mouseDevice;
    private readonly object _touchDevice;
    private readonly object _inputManager;

    private RawInputModifiers _activeButtons;
    private RawInputModifiers _activeKeyModifiers;
    private Point _lastPosition;

    public PointerSimulator()
    {
        var b = Bindings.Value;
        var id = System.Threading.Interlocked.Increment(ref _nextPointerId);
        _pointer = b.PointerCtor.Invoke(new object[] { id, /* PointerType.Mouse */ 0, true });
        _mouseDevice = b.MouseDeviceCtor.Invoke(new object[] { _pointer });
        _touchDevice = b.TouchDeviceCtor.Invoke(Array.Empty<object>());

        var locatorCurrent = b.LocatorCurrentProp.GetValue(null)
            ?? throw new InvalidOperationException("AvaloniaLocator.Current is null. Avalonia must be initialized first.");
        _inputManager = b.GetServiceMethod.Invoke(locatorCurrent, new object?[] { b.IInputManagerType })
            ?? throw new InvalidOperationException("IInputManager not registered in AvaloniaLocator.");
    }

    /// <summary>Last reported pointer position in window coordinates.</summary>
    public Point Position => _lastPosition;

    public Task MoveAsync(Window window, double x, double y)
        => DispatchPointerAsync(window, Move, new Point(x, y));

    public Task DownAsync(Window window, double x, double y, MouseButton button = MouseButton.Left)
    {
        var type = button switch
        {
            MouseButton.Left => LeftButtonDown,
            MouseButton.Right => RightButtonDown,
            MouseButton.Middle => MiddleButtonDown,
            MouseButton.XButton1 => XButton1Down,
            MouseButton.XButton2 => XButton2Down,
            _ => LeftButtonDown
        };
        _activeButtons |= ButtonToModifier(button);
        return DispatchPointerAsync(window, type, new Point(x, y));
    }

    public async Task UpAsync(Window window, double x, double y, MouseButton button = MouseButton.Left)
    {
        var type = button switch
        {
            MouseButton.Left => LeftButtonUp,
            MouseButton.Right => RightButtonUp,
            MouseButton.Middle => MiddleButtonUp,
            MouseButton.XButton1 => XButton1Up,
            MouseButton.XButton2 => XButton2Up,
            _ => LeftButtonUp
        };
        await DispatchPointerAsync(window, type, new Point(x, y));
        _activeButtons &= ~ButtonToModifier(button);
    }

    public async Task ClickAsync(Window window, double x, double y, MouseButton button = MouseButton.Left)
    {
        await MoveAsync(window, x, y);
        await DownAsync(window, x, y, button);
        await UpAsync(window, x, y, button);
    }

    public async Task DoubleClickAsync(Window window, double x, double y, MouseButton button = MouseButton.Left)
    {
        await ClickAsync(window, x, y, button);
        await ClickAsync(window, x, y, button);
    }

    public Task RightClickAsync(Window window, double x, double y) => ClickAsync(window, x, y, MouseButton.Right);

    /// <summary>
    /// Press at (x1,y1), interpolate movement to (x2,y2) over <paramref name="steps"/>
    /// frames, then release. The intermediate moves are required for Avalonia to
    /// recognize the gesture as a drag rather than a click.
    /// </summary>
    public async Task DragAsync(
        Window window,
        double x1, double y1,
        double x2, double y2,
        int steps = 10,
        int stepDelayMs = 16,
        MouseButton button = MouseButton.Left)
    {
        await MoveAsync(window, x1, y1);
        await DownAsync(window, x1, y1, button);

        steps = Math.Max(1, steps);
        for (int i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            await MoveAsync(window, x1 + (x2 - x1) * t, y1 + (y2 - y1) * t);
            if (stepDelayMs > 0) await Task.Delay(stepDelayMs);
        }

        await UpAsync(window, x2, y2, button);
    }

    public async Task HoverAsync(Window window, double x, double y, int lingerMs = 250)
    {
        await MoveAsync(window, x, y);
        if (lingerMs > 0) await Task.Delay(lingerMs);
    }

    public Task LeaveAsync(Window window) => DispatchPointerAsync(window, LeaveWindow, _lastPosition);

    // ===== Mouse wheel =====

    public async Task WheelAsync(Window window, double x, double y, double deltaX, double deltaY)
    {
        var b = Bindings.Value;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var args = b.RawMouseWheelCtor.Invoke(new object[]
            {
                _mouseDevice,
                (ulong)Environment.TickCount64,
                window,
                new Point(x, y),
                new Vector(deltaX, deltaY),
                (int)(_activeButtons | _activeKeyModifiers)
            });
            b.ProcessInputMethod.Invoke(_inputManager, new[] { args });
            _lastPosition = new Point(x, y);
        });
    }

    // ===== Touchpad gestures =====

    public Task MagnifyAsync(Window window, double x, double y, double scaleDelta)
        => DispatchGestureAsync(window, Magnify, new Point(x, y), new Vector(scaleDelta, 0));

    public Task RotateAsync(Window window, double x, double y, double angleDeltaDegrees)
        => DispatchGestureAsync(window, Rotate, new Point(x, y), new Vector(angleDeltaDegrees, 0));

    public Task SwipeAsync(Window window, double x, double y, double deltaX, double deltaY)
        => DispatchGestureAsync(window, Swipe, new Point(x, y), new Vector(deltaX, deltaY));

    private async Task DispatchGestureAsync(Window window, int rawType, Point position, Vector delta)
    {
        var b = Bindings.Value;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var args = b.RawGestureCtor.Invoke(new object[]
            {
                _mouseDevice,
                (ulong)Environment.TickCount64,
                window,
                Enum.ToObject(b.RawPointerEventType, rawType),
                position,
                delta,
                Enum.ToObject(b.RawInputModifiersType, (int)(_activeButtons | _activeKeyModifiers))
            });
            b.ProcessInputMethod.Invoke(_inputManager, new[] { args });
            _lastPosition = position;
        });
    }

    // ===== Touch input =====

    public Task TouchDownAsync(Window window, double x, double y, long touchId = 1)
        => DispatchTouchAsync(window, TouchBegin, new Point(x, y), touchId);

    public Task TouchMoveAsync(Window window, double x, double y, long touchId = 1)
        => DispatchTouchAsync(window, TouchUpdate, new Point(x, y), touchId);

    public Task TouchUpAsync(Window window, double x, double y, long touchId = 1)
        => DispatchTouchAsync(window, TouchEnd, new Point(x, y), touchId);

    public Task TouchCancelAsync(Window window, long touchId = 1)
        => DispatchTouchAsync(window, TouchCancel, _lastPosition, touchId);

    public async Task TouchTapAsync(Window window, double x, double y, long touchId = 1)
    {
        await TouchDownAsync(window, x, y, touchId);
        await TouchUpAsync(window, x, y, touchId);
    }

    public async Task TouchDragAsync(
        Window window,
        double x1, double y1,
        double x2, double y2,
        int steps = 10,
        int stepDelayMs = 16,
        long touchId = 1)
    {
        await TouchDownAsync(window, x1, y1, touchId);
        steps = Math.Max(1, steps);
        for (int i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            await TouchMoveAsync(window, x1 + (x2 - x1) * t, y1 + (y2 - y1) * t, touchId);
            if (stepDelayMs > 0) await Task.Delay(stepDelayMs);
        }
        await TouchUpAsync(window, x2, y2, touchId);
    }

    private async Task DispatchTouchAsync(Window window, int rawType, Point position, long touchId)
    {
        var b = Bindings.Value;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var args = b.RawTouchCtor.Invoke(new object[]
            {
                _touchDevice,
                (ulong)Environment.TickCount64,
                window,
                Enum.ToObject(b.RawPointerEventType, rawType),
                position,
                Enum.ToObject(b.RawInputModifiersType, (int)_activeKeyModifiers),
                touchId
            });
            b.ProcessInputMethod.Invoke(_inputManager, new[] { args });
            _lastPosition = position;
        });
    }

    private async Task DispatchPointerAsync(Window window, int rawType, Point position)
    {
        var b = Bindings.Value;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var args = b.RawPointerCtor.Invoke(new object[]
            {
                _mouseDevice,
                (ulong)Environment.TickCount64,
                window,
                Enum.ToObject(b.RawPointerEventType, rawType),
                position,
                Enum.ToObject(b.RawInputModifiersType, (int)(_activeButtons | _activeKeyModifiers))
            });
            b.ProcessInputMethod.Invoke(_inputManager, new[] { args });
            _lastPosition = position;
        });
    }

    public void SetKeyboardModifiers(bool ctrl, bool shift, bool alt, bool meta)
    {
        var m = RawInputModifiers.None;
        if (ctrl) m |= RawInputModifiers.Control;
        if (shift) m |= RawInputModifiers.Shift;
        if (alt) m |= RawInputModifiers.Alt;
        if (meta) m |= RawInputModifiers.Meta;
        _activeKeyModifiers = m;
    }

    private static RawInputModifiers ButtonToModifier(MouseButton button) => button switch
    {
        MouseButton.Left => RawInputModifiers.LeftMouseButton,
        MouseButton.Right => RawInputModifiers.RightMouseButton,
        MouseButton.Middle => RawInputModifiers.MiddleMouseButton,
        MouseButton.XButton1 => RawInputModifiers.XButton1MouseButton,
        MouseButton.XButton2 => RawInputModifiers.XButton2MouseButton,
        _ => RawInputModifiers.LeftMouseButton
    };

    // ===== Reflection bindings (loaded once) =====

    private sealed class ReflectionBindings
    {
        public required Type RawPointerEventType { get; init; }
        public required Type RawInputModifiersType { get; init; }
        public required Type IInputManagerType { get; init; }
        public required ConstructorInfo PointerCtor { get; init; }
        public required ConstructorInfo MouseDeviceCtor { get; init; }
        public required ConstructorInfo TouchDeviceCtor { get; init; }
        public required ConstructorInfo RawPointerCtor { get; init; }
        public required ConstructorInfo RawMouseWheelCtor { get; init; }
        public required ConstructorInfo RawTouchCtor { get; init; }
        public required ConstructorInfo RawGestureCtor { get; init; }
        public required PropertyInfo LocatorCurrentProp { get; init; }
        public required MethodInfo GetServiceMethod { get; init; }
        public required MethodInfo ProcessInputMethod { get; init; }
    }

    private static ReflectionBindings LoadBindings()
    {
        var asm = typeof(global::Avalonia.Input.Pointer).Assembly;

        Type Get(string name) => asm.GetType(name)
            ?? throw new InvalidOperationException($"Type not found in Avalonia.Base: {name}");

        var pointerType = Get("Avalonia.Input.Pointer");
        var pointerTypeEnum = Get("Avalonia.Input.PointerType");
        var mouseDeviceType = Get("Avalonia.Input.MouseDevice");
        var touchDeviceType = Get("Avalonia.Input.TouchDevice");
        var rawPointerEventType = Get("Avalonia.Input.Raw.RawPointerEventType");
        var rawInputModifiersType = Get("Avalonia.Input.RawInputModifiers");
        var rawInputEventArgsType = Get("Avalonia.Input.Raw.RawInputEventArgs");
        var rawPointerArgsType = Get("Avalonia.Input.Raw.RawPointerEventArgs");
        var rawMouseWheelArgsType = Get("Avalonia.Input.Raw.RawMouseWheelEventArgs");
        var rawTouchArgsType = Get("Avalonia.Input.Raw.RawTouchEventArgs");
        var rawGestureArgsType = Get("Avalonia.Input.Raw.RawPointerGestureEventArgs");
        var inputDeviceType = Get("Avalonia.Input.IInputDevice");
        var inputRootType = Get("Avalonia.Input.IInputRoot");
        var locatorType = Get("Avalonia.AvaloniaLocator");
        var inputManagerType = Get("Avalonia.Input.IInputManager");
        var depResolverType = Get("Avalonia.IAvaloniaDependencyResolver");

        ConstructorInfo FindCtor(Type t, params Type[] paramTypes)
        {
            var ctor = t.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, paramTypes);
            if (ctor != null) return ctor;
            // Fallback: match by parameter count + parameter type names (interface params can be tricky)
            var match = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => c.GetParameters().Length == paramTypes.Length
                    && c.GetParameters().Zip(paramTypes).All(p =>
                        p.First.ParameterType == p.Second
                        || p.First.ParameterType.IsAssignableFrom(p.Second)
                        || p.Second.IsAssignableFrom(p.First.ParameterType)
                        || p.First.ParameterType.Name == p.Second.Name));
            return match ?? throw new InvalidOperationException(
                $"Constructor not found on {t.FullName} with {paramTypes.Length} args");
        }

        var pointerCtor = FindCtor(pointerType, typeof(int), pointerTypeEnum, typeof(bool));
        var mouseDeviceCtor = FindCtor(mouseDeviceType, pointerType);
        var touchDeviceCtor = FindCtor(touchDeviceType);

        var rawPointerCtor = FindCtor(rawPointerArgsType,
            inputDeviceType, typeof(ulong), inputRootType, rawPointerEventType, typeof(Point), rawInputModifiersType);

        var rawMouseWheelCtor = FindCtor(rawMouseWheelArgsType,
            inputDeviceType, typeof(ulong), inputRootType, typeof(Point), typeof(Vector), rawInputModifiersType);

        var rawTouchCtor = FindCtor(rawTouchArgsType,
            inputDeviceType, typeof(ulong), inputRootType, rawPointerEventType, typeof(Point), rawInputModifiersType, typeof(long));

        var rawGestureCtor = FindCtor(rawGestureArgsType,
            inputDeviceType, typeof(ulong), inputRootType, rawPointerEventType, typeof(Point), typeof(Vector), rawInputModifiersType);

        var locatorCurrentProp = locatorType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("AvaloniaLocator.Current property not found");

        var getServiceMethod = depResolverType.GetMethod("GetService", new[] { typeof(Type) })
            ?? throw new InvalidOperationException("IAvaloniaDependencyResolver.GetService not found");

        var processInputMethod = inputManagerType.GetMethod("ProcessInput", new[] { rawInputEventArgsType })
            ?? throw new InvalidOperationException("IInputManager.ProcessInput not found");

        return new ReflectionBindings
        {
            RawPointerEventType = rawPointerEventType,
            RawInputModifiersType = rawInputModifiersType,
            IInputManagerType = inputManagerType,
            PointerCtor = pointerCtor,
            MouseDeviceCtor = mouseDeviceCtor,
            TouchDeviceCtor = touchDeviceCtor,
            RawPointerCtor = rawPointerCtor,
            RawMouseWheelCtor = rawMouseWheelCtor,
            RawTouchCtor = rawTouchCtor,
            RawGestureCtor = rawGestureCtor,
            LocatorCurrentProp = locatorCurrentProp,
            GetServiceMethod = getServiceMethod,
            ProcessInputMethod = processInputMethod,
        };
    }
}
