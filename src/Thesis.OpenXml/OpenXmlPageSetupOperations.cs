using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static partial class OpenXmlMicroEditor
{
    private static OperationResult ApplyProfilePageSetup(OpenXmlEditContext context, ThesisOperation operation, bool writeChanges)
    {
        var profileSetup = context.Profile?.PageSetup;
        if (profileSetup is null || (profileSetup.PageSize is null && profileSetup.Margins is null))
        {
            return OperationError(operation, "profile_page_setup_missing");
        }

        if (!TryCreateEffectivePageSetup(profileSetup, operation.Format, out var setup, out var setupError))
        {
            return OperationError(operation, setupError);
        }

        var sectionProperties = GetOrCreateBodySectionProperties(context.Body);
        var before = PageSetupPreview(OpenXmlFormatReaderSection(sectionProperties));
        if (writeChanges)
        {
            ApplyPageSetup(sectionProperties, setup);
        }

        var after = writeChanges
            ? PageSetupPreview(OpenXmlFormatReaderSection(sectionProperties))
            : PageSetupPreview(setup);
        var result = OperationSuccess(operation, writeChanges ? "applied" : "preview");
        result.Matches.Add(new MatchInfo
        {
            Id = "section:0",
            Type = "section",
            PreviewBefore = before,
            PreviewAfter = after
        });
        return result;
    }

    private static ProfilePageSetup OpenXmlFormatReaderSection(SectionProperties section)
    {
        var pageSize = section.GetFirstChild<PageSize>();
        var margin = section.GetFirstChild<PageMargin>();
        return new ProfilePageSetup
        {
            PageSize = pageSize is null
                ? null
                : new PageSizeInfo
                {
                    WidthTwips = ToInt(pageSize.Width),
                    HeightTwips = ToInt(pageSize.Height),
                    Orientation = pageSize.Orient?.Value.ToString().ToLowerInvariant()
                },
            Margins = margin is null
                ? null
                : new PageMarginInfo
                {
                    TopTwips = ToInt(margin.Top),
                    RightTwips = ToInt(margin.Right),
                    BottomTwips = ToInt(margin.Bottom),
                    LeftTwips = ToInt(margin.Left),
                    HeaderTwips = ToInt(margin.Header),
                    FooterTwips = ToInt(margin.Footer),
                    GutterTwips = ToInt(margin.Gutter)
                }
        };
    }

    private static bool TryCreateEffectivePageSetup(
        ProfilePageSetup profileSetup,
        JsonNode? overrideFormat,
        out ProfilePageSetup setup,
        out string error)
    {
        setup = new ProfilePageSetup
        {
            PageSize = profileSetup.PageSize is null
                ? null
                : new PageSizeInfo
                {
                    WidthTwips = profileSetup.PageSize.WidthTwips,
                    HeightTwips = profileSetup.PageSize.HeightTwips,
                    Orientation = profileSetup.PageSize.Orientation
                },
            Margins = profileSetup.Margins is null
                ? null
                : new PageMarginInfo
                {
                    TopTwips = profileSetup.Margins.TopTwips,
                    RightTwips = profileSetup.Margins.RightTwips,
                    BottomTwips = profileSetup.Margins.BottomTwips,
                    LeftTwips = profileSetup.Margins.LeftTwips,
                    HeaderTwips = profileSetup.Margins.HeaderTwips,
                    FooterTwips = profileSetup.Margins.FooterTwips,
                    GutterTwips = profileSetup.Margins.GutterTwips
                },
            Headers = [.. profileSetup.Headers],
            Footers = [.. profileSetup.Footers]
        };

        if (overrideFormat is not null && overrideFormat is not JsonObject)
        {
            error = "target_value_invalid";
            return false;
        }

        setup.PageSize ??= new PageSizeInfo();
        setup.Margins ??= new PageMarginInfo();
        if (!ApplyPageSizeIntOverride(overrideFormat, setup.PageSize, "widthTwips", (target, value) => target.WidthTwips = value, out error)
            || !ApplyPageSizeIntOverride(overrideFormat, setup.PageSize, "heightTwips", (target, value) => target.HeightTwips = value, out error)
            || !ApplyPageSizeStringOverride(overrideFormat, setup.PageSize, "orientation", (target, value) => target.Orientation = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "topTwips", (target, value) => target.TopTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "rightTwips", (target, value) => target.RightTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "bottomTwips", (target, value) => target.BottomTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "leftTwips", (target, value) => target.LeftTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "headerTwips", (target, value) => target.HeaderTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "footerTwips", (target, value) => target.FooterTwips = value, out error)
            || !ApplyPageMarginIntOverride(overrideFormat, setup.Margins, "gutterTwips", (target, value) => target.GutterTwips = value, out error))
        {
            return false;
        }

        if (!IsValidTwips(setup.PageSize.WidthTwips)
            || !IsValidTwips(setup.PageSize.HeightTwips)
            || !IsValidTwips(setup.Margins.TopTwips)
            || !IsValidTwips(setup.Margins.RightTwips)
            || !IsValidTwips(setup.Margins.BottomTwips)
            || !IsValidTwips(setup.Margins.LeftTwips)
            || !IsValidTwips(setup.Margins.HeaderTwips)
            || !IsValidTwips(setup.Margins.FooterTwips)
            || !IsValidTwips(setup.Margins.GutterTwips))
        {
            error = "format_value_invalid";
            return false;
        }

        setup.PageSize.Orientation = NormalizePageOrientation(setup.PageSize.Orientation);
        if (setup.PageSize.Orientation == "\0")
        {
            error = "format_value_invalid";
            return false;
        }

        error = "";
        return true;
    }

    private static void ApplyPageSetup(SectionProperties section, ProfilePageSetup setup)
    {
        if (setup.PageSize is not null)
        {
            section.GetFirstChild<PageSize>()?.Remove();
            var pageSize = new PageSize();
            if (setup.PageSize.WidthTwips is not null)
            {
                pageSize.Width = (UInt32Value)(uint)setup.PageSize.WidthTwips.Value;
            }

            if (setup.PageSize.HeightTwips is not null)
            {
                pageSize.Height = (UInt32Value)(uint)setup.PageSize.HeightTwips.Value;
            }

            if (!string.IsNullOrWhiteSpace(setup.PageSize.Orientation))
            {
                pageSize.Orient = setup.PageSize.Orientation == "landscape"
                    ? PageOrientationValues.Landscape
                    : PageOrientationValues.Portrait;
            }

            InsertPageSize(section, pageSize);
        }

        if (setup.Margins is not null)
        {
            section.GetFirstChild<PageMargin>()?.Remove();
            var margin = new PageMargin();
            if (setup.Margins.TopTwips is not null)
            {
                margin.Top = setup.Margins.TopTwips.Value;
            }

            if (setup.Margins.RightTwips is not null)
            {
                margin.Right = (UInt32Value)(uint)setup.Margins.RightTwips.Value;
            }

            if (setup.Margins.BottomTwips is not null)
            {
                margin.Bottom = setup.Margins.BottomTwips.Value;
            }

            if (setup.Margins.LeftTwips is not null)
            {
                margin.Left = (UInt32Value)(uint)setup.Margins.LeftTwips.Value;
            }

            if (setup.Margins.HeaderTwips is not null)
            {
                margin.Header = (UInt32Value)(uint)setup.Margins.HeaderTwips.Value;
            }

            if (setup.Margins.FooterTwips is not null)
            {
                margin.Footer = (UInt32Value)(uint)setup.Margins.FooterTwips.Value;
            }

            if (setup.Margins.GutterTwips is not null)
            {
                margin.Gutter = (UInt32Value)(uint)setup.Margins.GutterTwips.Value;
            }

            var pageSize = section.GetFirstChild<PageSize>();
            if (pageSize is not null)
            {
                section.InsertAfter(margin, pageSize);
            }
            else
            {
                section.PrependChild(margin);
            }
        }
    }

    private static void InsertPageSize(SectionProperties section, PageSize pageSize)
    {
        var insertAfter = section
            .ChildElements
            .LastOrDefault(child => child is HeaderReference or FooterReference);
        if (insertAfter is not null)
        {
            section.InsertAfter(pageSize, insertAfter);
        }
        else
        {
            section.PrependChild(pageSize);
        }
    }

    private static SectionProperties GetOrCreateBodySectionProperties(Body body)
    {
        var section = body.Elements<SectionProperties>().LastOrDefault();
        if (section is not null)
        {
            return section;
        }

        section = new SectionProperties();
        body.AppendChild(section);
        return section;
    }

    private static bool ApplyPageSizeIntOverride(
        JsonNode? overrideFormat,
        PageSizeInfo pageSize,
        string propertyName,
        Action<PageSizeInfo, int> apply,
        out string error)
    {
        var value = OpenXmlOperationJson.GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(pageSize, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyPageSizeStringOverride(
        JsonNode? overrideFormat,
        PageSizeInfo pageSize,
        string propertyName,
        Action<PageSizeInfo, string> apply,
        out string error)
    {
        var value = OpenXmlOperationJson.GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(pageSize, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyPageMarginIntOverride(
        JsonNode? overrideFormat,
        PageMarginInfo margins,
        string propertyName,
        Action<PageMarginInfo, int> apply,
        out string error)
    {
        var value = OpenXmlOperationJson.GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(margins, value.Value);
        }

        error = "";
        return true;
    }

    private static string PageSetupPreview(ProfilePageSetup setup)
    {
        return ThesisJson.Serialize(new
        {
            pageSize = setup.PageSize,
            margins = setup.Margins
        });
    }

    private static bool IsValidTwips(int? value)
    {
        return value is null or >= 0;
    }

    private static string? NormalizePageOrientation(string? orientation)
    {
        return orientation?.ToLowerInvariant() switch
        {
            null => null,
            "portrait" => "portrait",
            "landscape" => "landscape",
            _ => "\0"
        };
    }
}
