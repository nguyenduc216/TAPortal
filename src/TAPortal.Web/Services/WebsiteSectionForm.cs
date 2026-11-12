using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using TAWebsite.Core.Content;

namespace TAPortal.Web.Services;

// Builds the JSON content of a section from the generated editor form. Field names: "c.<key>" for a single field and
// "c.<list>[<i>].<key>" for an item of a list (the editor renumbers items 0..n-1 before submit; the order of the numbers is
// the order on the site). Only the fields of the section type are read; every value is length-checked, links must be safe
// (SafeUrl) and images must reference an existing default asset or an uploaded media id.
public static class WebsiteSectionForm
{
    public const int MaxText = 4000;
    public const int MaxHtml = 400_000;

    public static (JsonObject Content, List<string> Errors) Read(SectionType type, IFormCollection form)
    {
        var errors = new List<string>();
        var content = new JsonObject();
        foreach (var field in type.Fields)
        {
            if (field.Kind == FieldKind.List)
            {
                var items = new JsonArray();
                var prefix = $"c.{field.Key}[";
                var indexes = form.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(k => k[prefix.Length..]).Select(k => k[..Math.Max(0, k.IndexOf(']'))])
                    .Select(k => int.TryParse(k, out var i) ? i : -1).Where(i => i >= 0).Distinct().Order().ToList();
                if (indexes.Count > field.MaxItems) errors.Add($"{field.Label}: tối đa {field.MaxItems} mục.");
                foreach (var i in indexes.Take(field.MaxItems))
                {
                    var item = new JsonObject();
                    foreach (var sub in field.Items ?? [])
                        item[sub.Key] = Value(sub, form[$"c.{field.Key}[{i}].{sub.Key}"].ToString(), $"{field.Label} #{i + 1} – {sub.Label}", errors);
                    if (item.Any(p => !string.IsNullOrWhiteSpace(p.Value?.GetValue<string>()))) items.Add(item);
                }
                content[field.Key] = items;
            }
            else content[field.Key] = Value(field, form[$"c.{field.Key}"].ToString(), field.Label, errors);
        }
        return (content, errors);
    }

    private static string Value(FieldDef field, string raw, string label, List<string> errors)
    {
        if (field.Kind == FieldKind.Html)
        {
            if (raw.Length > MaxHtml) { errors.Add($"{label}: nội dung quá dài (tối đa {MaxHtml / 1000} nghìn ký tự)."); return ""; }
            var html = PostHtml.SanitizeSection(raw.Replace("/WebsiteMedia/File/", "/media/"));
            if (string.IsNullOrWhiteSpace(PostHtml.PlainText(html)) && !html.Contains("<img", StringComparison.OrdinalIgnoreCase)) errors.Add($"{label}: nội dung trống.");
            return html;
        }
        var value = field.Kind is FieldKind.TextArea or FieldKind.Lines ? raw.Replace("\r\n", "\n").Trim() : raw.Trim();
        if (value.Length > MaxText) { errors.Add($"{label}: tối đa {MaxText} ký tự."); value = value[..MaxText]; }
        if (field.Kind == FieldKind.Url && !SafeUrl.IsAllowed(value)) { errors.Add($"{label}: liên kết không hợp lệ (chỉ dùng /đường-dẫn, #mục, https://, tel:, mailto:)."); value = ""; }
        if (field.Kind == FieldKind.Image && !ImageRef.IsValid(value)) { errors.Add($"{label}: ảnh không hợp lệ, hãy chọn lại."); value = ""; }
        return value;
    }
}
