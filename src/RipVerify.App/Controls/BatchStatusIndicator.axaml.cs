using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using RipVerify.Core.Models;
using Path = Avalonia.Controls.Shapes.Path;

namespace RipVerify.App.Controls;

public partial class BatchStatusIndicator : UserControl
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#89B4FA"));
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.Parse("#FF5C4D"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#414561"));
    private static readonly IBrush VerifiedBrush = new SolidColorBrush(Color.Parse("#2ECC71"));

    public static readonly StyledProperty<BatchItemState> StateProperty =
        AvaloniaProperty.Register<BatchStatusIndicator, BatchItemState>(
            nameof(State),
            BatchItemState.Pending);

    public static readonly StyledProperty<IBrush?> CompletedBrushProperty =
        AvaloniaProperty.Register<BatchStatusIndicator, IBrush?>(nameof(CompletedBrush));

    public static readonly StyledProperty<bool> IsCancellingProperty =
        AvaloniaProperty.Register<BatchStatusIndicator, bool>(nameof(IsCancelling));

    private readonly Grid _spinnerHost;
    private readonly Arc _spinnerStroke;
    private readonly Ellipse _statusRing;
    private readonly Path _checkIcon;
    private readonly Path _errorIcon;
    private readonly Path _cancelledIcon;
    private readonly Ellipse _pendingIcon;
    private readonly RotateTransform _rotateTransform;
    private readonly DispatcherTimer _timer;

    public BatchStatusIndicator()
    {
        InitializeComponent();

        _spinnerHost = this.FindControl<Grid>("SpinnerHost")!;
        _spinnerStroke = this.FindControl<Arc>("SpinnerStroke")!;
        _statusRing = this.FindControl<Ellipse>("StatusRing")!;
        _checkIcon = this.FindControl<Path>("CheckIcon")!;
        _errorIcon = this.FindControl<Path>("ErrorIcon")!;
        _cancelledIcon = this.FindControl<Path>("CancelledIcon")!;
        _pendingIcon = this.FindControl<Ellipse>("PendingIcon")!;

        _rotateTransform = (RotateTransform)_spinnerHost.RenderTransform!;
        _rotateTransform.CenterX = 16;
        _rotateTransform.CenterY = 16;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTimerTick;

        UpdateStatus(State);
    }

    public BatchItemState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public IBrush? CompletedBrush
    {
        get => GetValue(CompletedBrushProperty);
        set => SetValue(CompletedBrushProperty, value);
    }

    public bool IsCancelling
    {
        get => GetValue(IsCancellingProperty);
        set => SetValue(IsCancellingProperty, value);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _rotateTransform.Angle = (_rotateTransform.Angle + 8) % 360;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StateProperty ||
            change.Property == CompletedBrushProperty ||
            change.Property == IsCancellingProperty)
        {
            UpdateStatus(State);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateStatus(State);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateStatus(BatchItemState status)
    {
        var isActive = status is BatchItemState.Scanning
            or BatchItemState.FixingCue
            or BatchItemState.VerifyingCueTools
            or BatchItemState.AnalyzingSpectrum
            or BatchItemState.ReadingOriginSignals
            or BatchItemState.VerifyingLog
            or BatchItemState.Classifying;

        var completedBrush = CompletedBrush ?? VerifiedBrush;

        _spinnerHost.IsVisible = isActive;
        _spinnerStroke.Stroke = IsCancelling ? DangerBrush : AccentBrush;
        _checkIcon.IsVisible = status == BatchItemState.Completed;
        _checkIcon.Stroke = completedBrush;
        _errorIcon.IsVisible = status == BatchItemState.Failed;
        _cancelledIcon.IsVisible = status == BatchItemState.Cancelled;
        _pendingIcon.IsVisible = status == BatchItemState.Pending;

        _statusRing.Stroke = status switch
        {
            BatchItemState.Completed => completedBrush,
            BatchItemState.Failed => DangerBrush,
            BatchItemState.Cancelled => MutedBrush,
            _ when IsCancelling => DangerBrush,
            _ => MutedBrush
        };

        if (isActive)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _rotateTransform.Angle = 0;
        }
    }
}
