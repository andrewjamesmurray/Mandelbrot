using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mandelbrot;

/// <summary>
/// Renders frames on a dedicated thread and hands them to the UI through a bounded
/// queue.
///
/// The auto-zoom is completely determined by the centre, the starting scale and the
/// ratio, so frames can be produced as fast as the GPU manages and consumed at the
/// display's rate. While frames are cheap the queue fills and banks the surplus;
/// when they turn expensive the bank is drawn down. The surplus per frame is capped
/// at one frame budget while the deficit is not - at 1e-16 a single frame costs
/// about twenty - so this buys depth rather than unlimited depth. Past the point
/// where the bank runs dry the descent simply slows; see MainWindow, which drives the
/// displayed scale from the frames that actually arrive.
///
/// The thread owns the accelerator and the <see cref="MandelbrotState"/> outright.
/// The UI never touches either; it posts commands and reads what comes back on the
/// frames themselves.
/// </summary>
public sealed class FramePump : IDisposable
{
    /// <summary>
    /// How many finished frames may be banked. Each is width*height*4 bytes, so 192
    /// frames is about 1.6 GB at 1080p and roughly 3 seconds of banked playback.
    /// Raising it extends how deep the zoom keeps full speed before it starts to slow.
    /// </summary>
    public const int QueueCapacity = 192;

    private readonly int _width;
    private readonly int _height;
    private readonly uint[] _palette;

    private readonly BlockingCollection<RenderedFrame> _finished = new(QueueCapacity);
    private readonly ConcurrentQueue<Action<MandelbrotState>> _commands = new();
    private readonly ConcurrentBag<uint[]> _spareBuffers = [];
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _thread;

    private volatile bool _zooming;
    private volatile bool _useGpu = true;
    private volatile bool _swapRequested;
    private volatile string _adapterDescription = "starting";

    public FramePump(int width, int height, uint[] palette)
    {
        _width = width;
        _height = height;
        _palette = palette;

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Mandelbrot render",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public string AdapterDescription => _adapterDescription;

    public bool IsZooming => _zooming;

    public int QueueDepth => _finished.Count;

    /// <summary>Mutates the view. Anything already queued is discarded, since it no
    /// longer shows what the user asked for.</summary>
    public void Post(Action<MandelbrotState> command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    public void StartZoom()
    {
        Post(state => state.ResetForZoom());
        _zooming = true;
        _wake.Set();
    }

    public void StopZoom()
    {
        _zooming = false;
        _wake.Set();
    }

    public void SwapAdapter()
    {
        _swapRequested = true;
        Post(_ => { });
    }

    public bool TryTake(out RenderedFrame? frame) => _finished.TryTake(out frame);

    /// <summary>Returns a consumed frame's buffer to the pool.</summary>
    public void Recycle(RenderedFrame frame) => _spareBuffers.Add(frame.Pixels);

    private void Run()
    {
        var state = new MandelbrotState(_width, _height);
        var adapter = CreateAdapter();
        _adapterDescription = adapter.Description;

        var timer = new Stopwatch();
        var pending = true;

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (DrainCommands(state))
                {
                    Flush();
                    pending = true;
                }

                if (_swapRequested)
                {
                    _swapRequested = false;
                    _useGpu = !_useGpu;
                    adapter.Dispose();
                    adapter = CreateAdapter();
                    _adapterDescription = adapter.Description;
                    Flush();
                    pending = true;
                }

                // Only a frame that followed an actual zoom step continues the
                // auto-zoom. The first frame after Space jumps to a whole new view,
                // and the display must snap onto it rather than magnify toward it.
                var advanced = false;
                if (_zooming && !pending)
                {
                    if (state.ZoomNext())
                        advanced = true;
                    else
                        _zooming = false;
                }

                if (!advanced && !pending)
                {
                    _wake.Wait(50, _shutdown.Token);
                    _wake.Reset();
                    continue;
                }

                pending = false;
                Produce(state, adapter, timer, fromAutoZoom: advanced);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            adapter.Dispose();
        }
    }

    private IComputeAdapter CreateAdapter() => _useGpu
        ? GpuAdapter.Create(_width, _height, _palette)
        : CpuAdapter.Create(_width, _height, _palette);

    private void Produce(MandelbrotState state, IComputeAdapter adapter, Stopwatch timer, bool fromAutoZoom)
    {
        if (!_spareBuffers.TryTake(out var pixels))
            pixels = new uint[_width * _height];

        timer.Restart();
        var (parameters, orbit) = state.PrepareFrame();
        adapter.Render(parameters, state.Mode, state.Optimizations, orbit, pixels);
        timer.Stop();

        var frame = new RenderedFrame
        {
            Pixels = pixels,
            Scale = state.Scale,
            LogScale = Math.Log(state.Scale),
            MaxIter = state.MaxIter,
            SkippedIterations = state.SkippedIterations,
            Mode = state.Mode,
            RenderMs = timer.Elapsed.TotalMilliseconds,
            FromAutoZoom = fromAutoZoom
        };

        // Back-pressure, but never at the cost of ignoring the user: if the queue is
        // full and something has been posted, drop this frame and go handle it.
        while (!_finished.TryAdd(frame, 5))
        {
            if (_shutdown.IsCancellationRequested || !_commands.IsEmpty || _swapRequested || !_zooming)
            {
                _spareBuffers.Add(pixels);
                return;
            }
        }
    }

    private bool DrainCommands(MandelbrotState state)
    {
        var ran = false;
        while (_commands.TryDequeue(out var command))
        {
            command(state);
            ran = true;
        }

        return ran;
    }

    private void Flush()
    {
        while (_finished.TryTake(out var stale))
            _spareBuffers.Add(stale.Pixels);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(5));

        _shutdown.Dispose();
        _wake.Dispose();
        _finished.Dispose();
    }
}
