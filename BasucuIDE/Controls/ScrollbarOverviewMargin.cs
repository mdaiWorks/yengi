using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using mdaiAgent;

namespace BasucuIDE.Controls
{
    /// <summary>
    /// VS Code / Visual Studio tarzı dikey Scrollbar Overview Margin katmanı.
    /// Hem canlı LSP / Linter hatalarını (kırmızı/sarı) hem de Ctrl+F arama eşleşmelerini (turuncu) gösterir.
    /// </summary>
    public class ScrollbarOverviewMargin : FrameworkElement
    {
        private readonly TextEditor _editor;
        private IReadOnlyList<LanguageDiagnostic> _diagnostics = Array.Empty<LanguageDiagnostic>();
        private IReadOnlyList<int> _searchMatchLines = Array.Empty<int>();

        // Renk paleti
        private static readonly Brush ErrorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ef4444"));
        private static readonly Brush WarningBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b"));
        private static readonly Brush SearchMatchBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#fbbf24"));
        private static readonly Brush ViewportIndicatorBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        private static readonly Pen ViewportBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.0);

        static ScrollbarOverviewMargin()
        {
            ErrorBrush.Freeze();
            WarningBrush.Freeze();
            SearchMatchBrush.Freeze();
            ViewportIndicatorBrush.Freeze();
            ViewportBorderPen.Freeze();
        }

        public ScrollbarOverviewMargin(TextEditor editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));

            IsHitTestVisible = true;
            Width = 14;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Stretch;

            _editor.SizeChanged += (s, e) => InvalidateVisual();
            _editor.OptionChanged += (s, e) => InvalidateVisual();

            if (_editor.TextArea != null && _editor.TextArea.TextView != null)
            {
                _editor.TextArea.TextView.ScrollOffsetChanged += (s, e) => InvalidateVisual();
            }

            _editor.Loaded += (s, e) => InvalidateVisual();

            MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        public void SetDiagnostics(IReadOnlyList<LanguageDiagnostic>? diagnostics)
        {
            _diagnostics = diagnostics ?? Array.Empty<LanguageDiagnostic>();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(InvalidateVisual));
        }

        public void SetSearchMatches(IEnumerable<int>? searchMatchLines)
        {
            _searchMatchLines = searchMatchLines?.Distinct().ToList() ?? (IReadOnlyList<int>)Array.Empty<int>();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(InvalidateVisual));
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (_editor.Document == null || _editor.Document.LineCount <= 0 || RenderSize.Height <= 0)
                return;

            int totalLines = _editor.Document.LineCount;

            // Scrollbar geometrisini ve iz (track) yüksekliğini hesapla
            double topOffset = 0;
            double trackHeight = RenderSize.Height;

            var scrollViewer = _editor.Template?.FindName("PART_ScrollViewer", _editor) as ScrollViewer;
            if (scrollViewer != null)
            {
                var scrollBar = scrollViewer.Template?.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar;
                if (scrollBar != null && scrollBar.IsVisible && scrollBar.ActualHeight > 0)
                {
                    try
                    {
                        Point point = scrollBar.TranslatePoint(new Point(0, 0), this);
                        topOffset = Math.Max(0, point.Y);
                        trackHeight = scrollBar.ActualHeight;
                    }
                    catch
                    {
                        topOffset = 0;
                        trackHeight = RenderSize.Height;
                    }
                }
            }

            if (trackHeight <= 0) return;

            // 1. Görünür Görünüm Alanı (Viewport Indicator - Şeffaf kutu)
            if (scrollViewer != null && scrollViewer.ExtentHeight > 0)
            {
                double visibleRatio = scrollViewer.ViewportHeight / Math.Max(1, scrollViewer.ExtentHeight);
                double currentOffsetRatio = scrollViewer.VerticalOffset / Math.Max(1, scrollViewer.ExtentHeight);

                double vpY = topOffset + (currentOffsetRatio * trackHeight);
                double vpH = Math.Max(10, visibleRatio * trackHeight);
                dc.DrawRectangle(ViewportIndicatorBrush, ViewportBorderPen, new Rect(0, vpY, Width, vpH));
            }

            // 2. Canlı LSP / Linter Hata ve Uyarı Çizgileri
            if (_diagnostics != null && _diagnostics.Count > 0)
            {
                foreach (var diag in _diagnostics)
                {
                    int line = Math.Clamp(diag.Line, 1, totalLines);
                    double lineRatio = totalLines > 1 ? (double)(line - 1) / (totalLines - 1) : 0;
                    double y = topOffset + (lineRatio * (trackHeight - 3));

                    bool isWarning = diag.Message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                                     diag.Message.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
                                     diag.Message.Contains("olası", StringComparison.OrdinalIgnoreCase);

                    Brush brush = isWarning ? WarningBrush : ErrorBrush;
                    double tickWidth = isWarning ? 6 : 8;
                    double x = Width - tickWidth - 2;

                    dc.DrawRectangle(brush, null, new Rect(x, Math.Max(0, y), tickWidth, 3));
                }
            }

            // 3. Ctrl+F Arama Eşleşmeleri Çizgileri
            if (_searchMatchLines != null && _searchMatchLines.Count > 0)
            {
                foreach (int line in _searchMatchLines)
                {
                    int clampedLine = Math.Clamp(line, 1, totalLines);
                    double lineRatio = totalLines > 1 ? (double)(clampedLine - 1) / (totalLines - 1) : 0;
                    double y = topOffset + (lineRatio * (trackHeight - 3.5));

                    double tickWidth = 10;
                    double x = Width - tickWidth - 1;

                    dc.DrawRectangle(SearchMatchBrush, null, new Rect(x, Math.Max(0, y), tickWidth, 3.5));
                }
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_editor.Document == null || _editor.Document.LineCount <= 0) return;

            Point pos = e.GetPosition(this);
            int totalLines = _editor.Document.LineCount;

            double topOffset = 0;
            double trackHeight = RenderSize.Height;

            var scrollViewer = _editor.Template?.FindName("PART_ScrollViewer", _editor) as ScrollViewer;
            if (scrollViewer != null)
            {
                var scrollBar = scrollViewer.Template?.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar;
                if (scrollBar != null && scrollBar.IsVisible && scrollBar.ActualHeight > 0)
                {
                    try
                    {
                        Point point = scrollBar.TranslatePoint(new Point(0, 0), this);
                        topOffset = Math.Max(0, point.Y);
                        trackHeight = scrollBar.ActualHeight;
                    }
                    catch { }
                }
            }

            if (trackHeight <= 0) return;

            double relativeY = (pos.Y - topOffset) / trackHeight;
            relativeY = Math.Clamp(relativeY, 0.0, 1.0);

            int targetLine = (int)Math.Round(1 + (relativeY * (totalLines - 1)));
            targetLine = Math.Clamp(targetLine, 1, totalLines);

            try
            {
                _editor.ScrollToLine(targetLine);
                var docLine = _editor.Document.GetLineByNumber(targetLine);
                if (docLine != null)
                {
                    _editor.TextArea.Caret.Offset = docLine.Offset;
                    _editor.Focus();
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// ScrollbarOverviewMargin bileşenini TextEditor üzerine sağ kenarda katmanlayan WPF Adorner.
    /// </summary>
    public class OverviewMarginAdorner : System.Windows.Documents.Adorner
    {
        private readonly ScrollbarOverviewMargin _margin;
        private readonly VisualCollection _visuals;

        public OverviewMarginAdorner(TextEditor editor, ScrollbarOverviewMargin margin)
            : base(editor)
        {
            _margin = margin ?? throw new ArgumentNullException(nameof(margin));
            _visuals = new VisualCollection(this) { _margin };
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return _margin;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _margin.Measure(constraint);
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _margin.Arrange(new Rect(finalSize.Width - 14, 0, 14, finalSize.Height));
            return finalSize;
        }
    }
}
