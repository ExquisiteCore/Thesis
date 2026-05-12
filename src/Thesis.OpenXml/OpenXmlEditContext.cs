using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal sealed class OpenXmlEditContext(
    MainDocumentPart mainPart,
    Body body,
    HashSet<string> paragraphStyleIds,
    IReadOnlyDictionary<string, int> styleOutlineLevels,
    TemplateProfile? profile,
    JsonObject? profileOverrides)
{
    public MainDocumentPart MainPart { get; } = mainPart;

    public Body Body { get; } = body;

    public HashSet<string> ParagraphStyleIds { get; } = paragraphStyleIds;

    public IReadOnlyDictionary<string, int> StyleOutlineLevels { get; } = styleOutlineLevels;

    public OpenXmlTargetResolver Resolver { get; private set; } = CreateResolver(body, profile, profileOverrides, styleOutlineLevels);

    public TemplateProfile? Profile { get; } = profile;

    public JsonObject? ProfileOverrides { get; } = profileOverrides;

    public void RefreshResolver()
    {
        Resolver = CreateResolver(Body, Profile, ProfileOverrides, StyleOutlineLevels);
    }

    private static OpenXmlTargetResolver CreateResolver(
        Body body,
        TemplateProfile? profile,
        JsonObject? profileOverrides,
        IReadOnlyDictionary<string, int> styleOutlineLevels)
    {
        return new OpenXmlTargetResolver(body, profile, profileOverrides, styleOutlineLevels);
    }
}
