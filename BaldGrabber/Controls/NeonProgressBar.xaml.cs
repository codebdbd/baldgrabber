using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using Windows.UI;

namespace BaldGrabber.Controls;

public sealed partial class NeonProgressBar : UserControl
{
    private Storyboard? _glowStoryboard;
    private DispatcherTimer? _shimmerTimer;

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(NeonProgressBar),
            new PropertyMetadata(Colors.Purple, OnAccentColorChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NeonProgressBar),
            new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(NeonProgressBar),
            new PropertyMetadata(false, OnIsActiveChanged));

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public NeonProgressBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateColors();
        StartShimmerTimer();
        UpdateWidth(Value);
    }

    private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NeonProgressBar bar)
            bar.UpdateColors();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NeonProgressBar bar)
            bar.UpdateWidth((double)e.NewValue);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NeonProgressBar bar)
        {
            if ((bool)e.NewValue)
                bar.StartGlow();
            else
                bar.StopGlow();
        }
    }

    private void UpdateColors()
    {
        var color = AccentColor;
        var lighter = Color.FromArgb(255,
            (byte)Math.Min(255, color.R + 60),
            (byte)Math.Min(255, color.G + 60),
            (byte)Math.Min(255, color.B + 60));

        GradStart.Color = color;
        GradMid.Color = lighter;
        GradEnd.Color = color;
        GlowBorder.Background = new SolidColorBrush(color);
    }

    private void UpdateWidth(double value)
    {
        var parentWidth = ActualWidth > 0 ? ActualWidth : 400;
        var targetWidth = Math.Max(0, Math.Min(parentWidth, parentWidth * value));
        ProgressFill.Width = targetWidth;
    }

    private void StartShimmerTimer()
    {
        _shimmerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _shimmerTimer.Tick += (s, e) => PlayShimmer();
        _shimmerTimer.Start();
    }

    private void PlayShimmer()
    {
        if (ProgressFill.Width <= 0) return;

        Shimmer.Width = ProgressFill.Width;
        var animation = new DoubleAnimation
        {
            From = -ProgressFill.Width,
            To = ProgressFill.Width,
            Duration = new Duration(TimeSpan.FromMilliseconds(800)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var transform = Shimmer.RenderTransform as TranslateTransform;
        if (transform == null)
        {
            transform = new TranslateTransform();
            Shimmer.RenderTransform = transform;
        }

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, "X");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void StartGlow()
    {
        _glowStoryboard?.Stop();

        GlowBorder.Background = new SolidColorBrush(AccentColor);

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 0.6,
            Duration = new Duration(TimeSpan.FromMilliseconds(400))
        };

        var pulse = new DoubleAnimation
        {
            From = 0.4,
            To = 0.8,
            Duration = new Duration(TimeSpan.FromMilliseconds(1200)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        _glowStoryboard = new Storyboard();
        Storyboard.SetTarget(fadeIn, GlowBorder);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        _glowStoryboard.Children.Add(fadeIn);
        _glowStoryboard.Begin();

        var pulseStoryboard = new Storyboard();
        Storyboard.SetTarget(pulse, GlowBorder);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        pulseStoryboard.Children.Add(pulse);

        fadeIn.Completed += (s, e) =>
        {
            _glowStoryboard = pulseStoryboard;
            pulseStoryboard.Begin();
        };
    }

    private void StopGlow()
    {
        _glowStoryboard?.Stop();

        var fadeOut = new DoubleAnimation
        {
            From = GlowBorder.Opacity,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300))
        };

        _glowStoryboard = new Storyboard();
        Storyboard.SetTarget(fadeOut, GlowBorder);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        _glowStoryboard.Children.Add(fadeOut);
        _glowStoryboard.Begin();
    }
}
