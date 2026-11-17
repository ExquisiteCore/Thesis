using System.Xml.Linq;
using DocumentFormat.OpenXml.Math;
using Thesis.Core;

namespace Thesis.OpenXml;

internal static class LatexOfficeMath
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    public static OfficeMath Create(string source) => new(new XElement(M + "oMath", Write(LatexMath.Parse(source))).ToString(SaveOptions.DisableFormatting));

    private static IEnumerable<XElement> Write(MathNode node)
    {
        XElement Element(string name, MathNode value) => new(M + name, Write(value));
        switch (node)
        {
            case MathText text:
                yield return new XElement(M + "r", new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text.Value)); break;
            case MathSequence sequence:
                foreach (var child in sequence.Children) foreach (var element in Write(child)) yield return element;
                break;
            case MathFraction fraction:
                yield return new XElement(M + "f", Element("num", fraction.Numerator), Element("den", fraction.Denominator)); break;
            case MathRadical radical:
                yield return new XElement(M + "rad", new XElement(M + "radPr", new XElement(M + "degHide", new XAttribute(M + "val", radical.Degree is null ? "1" : "0"))),
                    radical.Degree is null ? new XElement(M + "deg") : Element("deg", radical.Degree), Element("e", radical.Value)); break;
            case MathScript script when script.Superscript is not null && script.Subscript is not null:
                yield return new XElement(M + "sSubSup", Element("e", script.Value), Element("sub", script.Subscript), Element("sup", script.Superscript)); break;
            case MathScript script:
                yield return new XElement(M + (script.Superscript is null ? "sSub" : "sSup"), Element("e", script.Value),
                    Element(script.Superscript is null ? "sub" : "sup", script.Superscript ?? script.Subscript!)); break;
        }
    }
}
