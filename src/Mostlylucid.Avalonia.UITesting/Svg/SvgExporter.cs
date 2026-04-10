using System.Globalization;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;

namespace Mostlylucid.Avalonia.UITesting.Svg;

public sealed class SvgExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private int _defsCounter;
    private readonly List<Action<XmlWriter>> _deferredDefs = new();

    public string Export(Visual root)
    {
        _defsCounter = 0;
        _deferredDefs.Clear();

        var bounds = root is Control c ? c.Bounds : new Rect(0, 0, 800, 600);
        var width = bounds.Width;
        var height = bounds.Height;

        // First pass: collect deferred defs (gradients etc.)
        // We do a two-pass approach: serialize body to a temp buffer, then write defs + body
        var bodyWriter = new StringWriter();
        using (var bodyXml = XmlWriter.Create(bodyWriter, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment }))
        {
            WriteVisual(bodyXml, root, root);
        }
        var bodyContent = bodyWriter.ToString();

        // Final output
        using var ms = new MemoryStream();
        using var xml = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false,
            Encoding = new System.Text.UTF8Encoding(false)
        });

        xml.WriteStartDocument();
        xml.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        xml.WriteAttributeString("width", F(width));
        xml.WriteAttributeString("height", F(height));
        xml.WriteAttributeString("viewBox", $"0 0 {F(width)} {F(height)}");

        if (_deferredDefs.Count > 0)
        {
            xml.WriteStartElement("defs");
            foreach (var writeDef in _deferredDefs)
                writeDef(xml);
            xml.WriteEndElement();
        }

        xml.WriteRaw(bodyContent);

        xml.WriteEndElement(); // svg
        xml.WriteEndDocument();
        xml.Flush();

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private void WriteVisual(XmlWriter xml, Visual visual, Visual root)
    {
        if (visual is Control ctrl && !ctrl.IsVisible)
            return;

        // Skip fully transparent visuals entirely
        if (visual.Opacity <= 0)
            return;

        var transform = visual.TransformToVisual(root);
        if (transform == null) return;

        var bounds = visual is Control ctl ? ctl.Bounds : new Rect();
        var topLeft = new Point(0, 0).Transform(transform.Value);

        var hasGroup = NeedsGroup(visual);
        if (hasGroup)
        {
            xml.WriteStartElement("g");

            if (visual.Opacity < 1.0)
                xml.WriteAttributeString("opacity", F2(visual.Opacity));

            if (visual.RenderTransform != null)
            {
                var m = visual.RenderTransform.Value;
                if (m != Matrix.Identity)
                    xml.WriteAttributeString("transform",
                        $"matrix({F(m.M11)},{F(m.M12)},{F(m.M21)},{F(m.M22)},{F(m.M31)},{F(m.M32)})");
            }
        }

        WriteControl(xml, visual, topLeft, bounds);

        foreach (var child in visual.GetVisualChildren())
            WriteVisual(xml, child, root);

        if (hasGroup)
            xml.WriteEndElement();
    }

    private static bool NeedsGroup(Visual visual)
    {
        return visual.Opacity < 1.0
            || (visual.RenderTransform != null && visual.RenderTransform.Value != Matrix.Identity);
    }

    private void WriteControl(XmlWriter xml, Visual visual, Point position, Rect bounds)
    {
        switch (visual)
        {
            case Border border:
                WriteBorder(xml, border, position);
                break;
            case TextBlock textBlock:
                WriteTextBlock(xml, textBlock, position);
                break;
            case Rectangle rect:
                WriteRectangleShape(xml, rect, position);
                break;
            case Ellipse ellipse:
                WriteEllipseShape(xml, ellipse, position);
                break;
            case Line line:
                WriteLineShape(xml, line, position);
                break;
            case Path path:
                WritePathShape(xml, path, position);
                break;
            case Panel panel:
                WritePanel(xml, panel, position);
                break;
            case Image image:
                WriteImage(xml, image, position);
                break;
            case TemplatedControl tc:
                WriteTemplatedControl(xml, tc, position);
                break;
        }
    }

    private void WriteBorder(XmlWriter xml, Border border, Point pos)
    {
        var b = border.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        var hasFill = border.Background != null && !IsTransparent(border.Background);
        var hasStroke = border.BorderBrush != null && border.BorderThickness != default && !IsTransparent(border.BorderBrush);

        if (!hasFill && !hasStroke) return;

        xml.WriteStartElement("rect");
        xml.WriteAttributeString("x", F(pos.X));
        xml.WriteAttributeString("y", F(pos.Y));
        xml.WriteAttributeString("width", F(b.Width));
        xml.WriteAttributeString("height", F(b.Height));

        if (border.CornerRadius != default)
        {
            xml.WriteAttributeString("rx", F(border.CornerRadius.TopLeft));
            xml.WriteAttributeString("ry", F(border.CornerRadius.TopLeft));
        }

        WriteFill(xml, border.Background);

        if (hasStroke)
        {
            WriteStrokeBrush(xml, border.BorderBrush);
            xml.WriteAttributeString("stroke-width", SvgColorHelper.ThicknessToStrokeWidth(border.BorderThickness));
        }

        xml.WriteEndElement();
    }

    private void WritePanel(XmlWriter xml, Panel panel, Point pos)
    {
        if (panel.Background == null || IsTransparent(panel.Background)) return;
        var b = panel.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        xml.WriteStartElement("rect");
        xml.WriteAttributeString("x", F(pos.X));
        xml.WriteAttributeString("y", F(pos.Y));
        xml.WriteAttributeString("width", F(b.Width));
        xml.WriteAttributeString("height", F(b.Height));
        WriteFill(xml, panel.Background);
        xml.WriteEndElement();
    }

    private void WriteTextBlock(XmlWriter xml, TextBlock tb, Point pos)
    {
        var text = tb.Text;
        if (string.IsNullOrEmpty(text))
        {
            // Try Inlines for rich text - grab the plain text representation
            if (tb.Inlines is { Count: > 0 })
                text = tb.Text; // Avalonia resolves Inlines into Text
            if (string.IsNullOrEmpty(text)) return;
        }

        var fontSize = tb.FontSize;

        xml.WriteStartElement("text");
        xml.WriteAttributeString("x", F(pos.X));
        xml.WriteAttributeString("y", F(pos.Y + fontSize * 0.85));
        xml.WriteAttributeString("font-size", F(fontSize));

        if (tb.FontFamily?.Name != null)
            xml.WriteAttributeString("font-family", tb.FontFamily.Name);

        if (tb.FontWeight != FontWeight.Normal)
            xml.WriteAttributeString("font-weight", ((int)tb.FontWeight).ToString());

        if (tb.FontStyle != FontStyle.Normal)
            xml.WriteAttributeString("font-style", tb.FontStyle.ToString().ToLowerInvariant());

        WriteFill(xml, tb.Foreground);

        switch (tb.TextAlignment)
        {
            case TextAlignment.Center:
                xml.WriteAttributeString("text-anchor", "middle");
                break;
            case TextAlignment.Right:
                xml.WriteAttributeString("text-anchor", "end");
                break;
        }

        if (tb.TextDecorations != null)
        {
            var decorations = new List<string>();
            foreach (var d in tb.TextDecorations)
            {
                if (d.Location == TextDecorationLocation.Underline)
                    decorations.Add("underline");
                else if (d.Location == TextDecorationLocation.Strikethrough)
                    decorations.Add("line-through");
            }
            if (decorations.Count > 0)
                xml.WriteAttributeString("text-decoration", string.Join(" ", decorations));
        }

        xml.WriteString(text);
        xml.WriteEndElement();
    }

    private void WriteRectangleShape(XmlWriter xml, Rectangle rect, Point pos)
    {
        var b = rect.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        xml.WriteStartElement("rect");
        xml.WriteAttributeString("x", F(pos.X));
        xml.WriteAttributeString("y", F(pos.Y));
        xml.WriteAttributeString("width", F(b.Width));
        xml.WriteAttributeString("height", F(b.Height));

        if (rect.RadiusX > 0) xml.WriteAttributeString("rx", F(rect.RadiusX));
        if (rect.RadiusY > 0) xml.WriteAttributeString("ry", F(rect.RadiusY));

        WriteShapeAttributes(xml, rect);
        xml.WriteEndElement();
    }

    private void WriteEllipseShape(XmlWriter xml, Ellipse ellipse, Point pos)
    {
        var b = ellipse.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        xml.WriteStartElement("ellipse");
        xml.WriteAttributeString("cx", F(pos.X + b.Width / 2));
        xml.WriteAttributeString("cy", F(pos.Y + b.Height / 2));
        xml.WriteAttributeString("rx", F(b.Width / 2));
        xml.WriteAttributeString("ry", F(b.Height / 2));
        WriteShapeAttributes(xml, ellipse);
        xml.WriteEndElement();
    }

    private void WriteLineShape(XmlWriter xml, Line line, Point pos)
    {
        xml.WriteStartElement("line");
        xml.WriteAttributeString("x1", F(pos.X + line.StartPoint.X));
        xml.WriteAttributeString("y1", F(pos.Y + line.StartPoint.Y));
        xml.WriteAttributeString("x2", F(pos.X + line.EndPoint.X));
        xml.WriteAttributeString("y2", F(pos.Y + line.EndPoint.Y));
        WriteShapeAttributes(xml, line);
        xml.WriteEndElement();
    }

    private void WritePathShape(XmlWriter xml, Path path, Point pos)
    {
        if (path.Data == null) return;

        var pathData = GetGeometryPathData(path.Data);
        if (string.IsNullOrEmpty(pathData))
        {
            // Fallback: emit the bounding rect of the geometry
            var gb = path.Data.Bounds;
            if (gb.Width <= 0 || gb.Height <= 0) return;
            xml.WriteStartElement("rect");
            xml.WriteAttributeString("x", F(pos.X + gb.X));
            xml.WriteAttributeString("y", F(pos.Y + gb.Y));
            xml.WriteAttributeString("width", F(gb.Width));
            xml.WriteAttributeString("height", F(gb.Height));
            WriteShapeAttributes(xml, path);
            xml.WriteEndElement();
            return;
        }

        xml.WriteStartElement("path");
        if (pos.X != 0 || pos.Y != 0)
            xml.WriteAttributeString("transform", $"translate({F(pos.X)},{F(pos.Y)})");
        xml.WriteAttributeString("d", pathData);
        WriteShapeAttributes(xml, path);
        xml.WriteEndElement();
    }

    private static string? GetGeometryPathData(Geometry geometry)
    {
        // PathGeometry: reconstruct from Figures
        if (geometry is PathGeometry pg && pg.Figures is { Count: > 0 })
            return ReconstructPathData(pg);

        // StreamGeometry.ToString() returns the type name, not path data.
        // Try to detect if ToString() returns valid SVG path data (starts with M/m).
        var str = geometry.ToString();
        if (!string.IsNullOrEmpty(str) && str.Length > 1 && (str[0] == 'M' || str[0] == 'm'))
            return str;

        return null;
    }

    private static string ReconstructPathData(PathGeometry pg)
    {
        var sb = new System.Text.StringBuilder();
        if (pg.Figures is null) return sb.ToString();
        foreach (var figure in pg.Figures)
        {
            sb.Append($"M{F(figure.StartPoint.X)},{F(figure.StartPoint.Y)}");
            if (figure.Segments is null) continue;
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment ls:
                        sb.Append($" L{F(ls.Point.X)},{F(ls.Point.Y)}");
                        break;
                    case BezierSegment bs:
                        sb.Append($" C{F(bs.Point1.X)},{F(bs.Point1.Y)} {F(bs.Point2.X)},{F(bs.Point2.Y)} {F(bs.Point3.X)},{F(bs.Point3.Y)}");
                        break;
                    case QuadraticBezierSegment qbs:
                        sb.Append($" Q{F(qbs.Point1.X)},{F(qbs.Point1.Y)} {F(qbs.Point2.X)},{F(qbs.Point2.Y)}");
                        break;
                    case ArcSegment arc:
                        var sweep = arc.SweepDirection == SweepDirection.Clockwise ? 1 : 0;
                        var largeArc = arc.IsLargeArc ? 1 : 0;
                        sb.Append($" A{F(arc.Size.Width)},{F(arc.Size.Height)} {F(arc.RotationAngle)} {largeArc} {sweep} {F(arc.Point.X)},{F(arc.Point.Y)}");
                        break;
                    case PolyLineSegment pls:
                        foreach (var pt in pls.Points)
                            sb.Append($" L{F(pt.X)},{F(pt.Y)}");
                        break;
                }
            }
            if (figure.IsClosed) sb.Append(" Z");
        }
        return sb.ToString();
    }

    private void WriteImage(XmlWriter xml, Image image, Point pos)
    {
        var b = image.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        // Placeholder - actual image embedding would require base64 encoding
        xml.WriteStartElement("rect");
        xml.WriteAttributeString("x", F(pos.X));
        xml.WriteAttributeString("y", F(pos.Y));
        xml.WriteAttributeString("width", F(b.Width));
        xml.WriteAttributeString("height", F(b.Height));
        xml.WriteAttributeString("fill", "#cccccc");
        xml.WriteAttributeString("stroke", "#999999");
        xml.WriteAttributeString("stroke-width", "1");
        xml.WriteEndElement();

        xml.WriteStartElement("text");
        xml.WriteAttributeString("x", F(pos.X + b.Width / 2));
        xml.WriteAttributeString("y", F(pos.Y + b.Height / 2 + 4));
        xml.WriteAttributeString("text-anchor", "middle");
        xml.WriteAttributeString("font-size", "10");
        xml.WriteAttributeString("fill", "#666666");
        xml.WriteString("[image]");
        xml.WriteEndElement();
    }

    private void WriteTemplatedControl(XmlWriter xml, TemplatedControl tc, Point pos)
    {
        // For templated controls (Button, CheckBox, etc.), emit background if present.
        // Skip if a child Border will already handle rendering (avoids duplicates).
        if (tc.Background == null || IsTransparent(tc.Background)) return;
        if (tc.GetVisualChildren().Any(c => c is Border)) return;
        var b = tc.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        xml.WriteStartElement("rect");
        xml.WriteAttributeString("x", F(pos.X));
        xml.WriteAttributeString("y", F(pos.Y));
        xml.WriteAttributeString("width", F(b.Width));
        xml.WriteAttributeString("height", F(b.Height));

        if (tc.CornerRadius != default)
        {
            xml.WriteAttributeString("rx", F(tc.CornerRadius.TopLeft));
            xml.WriteAttributeString("ry", F(tc.CornerRadius.TopLeft));
        }

        WriteFill(xml, tc.Background);

        if (tc.BorderBrush != null && tc.BorderThickness != default)
        {
            WriteStrokeBrush(xml, tc.BorderBrush);
            xml.WriteAttributeString("stroke-width", SvgColorHelper.ThicknessToStrokeWidth(tc.BorderThickness));
        }

        xml.WriteEndElement();
    }

    private void WriteShapeAttributes(XmlWriter xml, Shape shape)
    {
        WriteFill(xml, shape.Fill);

        if (shape.Stroke != null)
        {
            WriteStrokeBrush(xml, shape.Stroke);
            xml.WriteAttributeString("stroke-width", F(shape.StrokeThickness));

            if (shape.StrokeDashArray is { Count: > 0 })
                xml.WriteAttributeString("stroke-dasharray",
                    string.Join(",", shape.StrokeDashArray.Select(d => F(d))));

            if (shape.StrokeLineCap != PenLineCap.Flat)
                xml.WriteAttributeString("stroke-linecap", shape.StrokeLineCap.ToString().ToLowerInvariant());

            if (shape.StrokeJoin != PenLineJoin.Miter)
                xml.WriteAttributeString("stroke-linejoin", shape.StrokeJoin.ToString().ToLowerInvariant());
        }
    }

    private void WriteFill(XmlWriter xml, IBrush? brush)
    {
        if (brush == null)
        {
            xml.WriteAttributeString("fill", "none");
            return;
        }

        switch (brush)
        {
            case ISolidColorBrush:
                xml.WriteAttributeString("fill", SvgColorHelper.BrushToSvgColor(brush));
                var opacity = SvgColorHelper.BrushOpacity(brush);
                if (opacity < 1.0)
                    xml.WriteAttributeString("fill-opacity", F2(opacity));
                break;

            case ILinearGradientBrush lgb:
                var lgId = RegisterLinearGradient(lgb);
                xml.WriteAttributeString("fill", $"url(#{lgId})");
                break;

            case IRadialGradientBrush rgb:
                var rgId = RegisterRadialGradient(rgb);
                xml.WriteAttributeString("fill", $"url(#{rgId})");
                break;

            default:
                xml.WriteAttributeString("fill", "none");
                break;
        }
    }

    private void WriteStrokeBrush(XmlWriter xml, IBrush? brush)
    {
        if (brush == null) return;

        switch (brush)
        {
            case ISolidColorBrush:
                xml.WriteAttributeString("stroke", SvgColorHelper.BrushToSvgColor(brush));
                var opacity = SvgColorHelper.BrushOpacity(brush);
                if (opacity < 1.0)
                    xml.WriteAttributeString("stroke-opacity", F2(opacity));
                break;

            case ILinearGradientBrush lgb:
                var lgId = RegisterLinearGradient(lgb);
                xml.WriteAttributeString("stroke", $"url(#{lgId})");
                break;

            case IRadialGradientBrush rgb:
                var rgId = RegisterRadialGradient(rgb);
                xml.WriteAttributeString("stroke", $"url(#{rgId})");
                break;

            default:
                xml.WriteAttributeString("stroke", "none");
                break;
        }
    }

    private string RegisterLinearGradient(ILinearGradientBrush lgb)
    {
        var id = $"lg{_defsCounter++}";
        _deferredDefs.Add(xml =>
        {
            xml.WriteStartElement("linearGradient");
            xml.WriteAttributeString("id", id);
            xml.WriteAttributeString("x1", F(lgb.StartPoint.Point.X));
            xml.WriteAttributeString("y1", F(lgb.StartPoint.Point.Y));
            xml.WriteAttributeString("x2", F(lgb.EndPoint.Point.X));
            xml.WriteAttributeString("y2", F(lgb.EndPoint.Point.Y));

            if (lgb.StartPoint.Unit == RelativeUnit.Relative)
                xml.WriteAttributeString("gradientUnits", "objectBoundingBox");

            foreach (var stop in lgb.GradientStops)
            {
                xml.WriteStartElement("stop");
                xml.WriteAttributeString("offset", F2(stop.Offset));
                xml.WriteAttributeString("stop-color",
                    $"#{stop.Color.R:x2}{stop.Color.G:x2}{stop.Color.B:x2}");
                if (stop.Color.A < 255)
                    xml.WriteAttributeString("stop-opacity", F2(stop.Color.A / 255.0));
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        });
        return id;
    }

    private string RegisterRadialGradient(IRadialGradientBrush rgb)
    {
        var id = $"rg{_defsCounter++}";
        _deferredDefs.Add(xml =>
        {
            xml.WriteStartElement("radialGradient");
            xml.WriteAttributeString("id", id);
            xml.WriteAttributeString("cx", F(rgb.Center.Point.X));
            xml.WriteAttributeString("cy", F(rgb.Center.Point.Y));
            // Use RadiusX as the SVG single-radius approximation (RadiusY ignored — SVG <radialGradient> r is scalar).
            xml.WriteAttributeString("r", F(rgb.RadiusX.Scalar));

            if (rgb.GradientOrigin != rgb.Center)
            {
                xml.WriteAttributeString("fx", F(rgb.GradientOrigin.Point.X));
                xml.WriteAttributeString("fy", F(rgb.GradientOrigin.Point.Y));
            }

            if (rgb.Center.Unit == RelativeUnit.Relative)
                xml.WriteAttributeString("gradientUnits", "objectBoundingBox");

            foreach (var stop in rgb.GradientStops)
            {
                xml.WriteStartElement("stop");
                xml.WriteAttributeString("offset", F2(stop.Offset));
                xml.WriteAttributeString("stop-color",
                    $"#{stop.Color.R:x2}{stop.Color.G:x2}{stop.Color.B:x2}");
                if (stop.Color.A < 255)
                    xml.WriteAttributeString("stop-opacity", F2(stop.Color.A / 255.0));
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        });
        return id;
    }

    private static bool IsTransparent(IBrush? brush)
    {
        if (brush == null) return true;
        if (brush is ISolidColorBrush scb)
            return scb.Color.A == 0 || scb.Opacity <= 0;
        return false;
    }

    private static string F(double value) => SvgColorHelper.F(value);
    private static string F2(double value) => SvgColorHelper.F2(value);
}