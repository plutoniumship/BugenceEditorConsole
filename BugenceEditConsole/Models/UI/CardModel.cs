using Microsoft.AspNetCore.Html;

namespace BugenceEditConsole.Models.UI;

public class CardModel
{
    public string? Id { get; set; }
    public string? Eyebrow { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string Variant { get; set; } = "default";
    public string HeadingTag { get; set; } = "h2";
    public string? Classes { get; set; }
    public string? ShellClasses { get; set; }
    public string? BodyClasses { get; set; }
    public IHtmlContent? HeaderActions { get; set; }
    public IHtmlContent? Body { get; set; }
    public IHtmlContent? Footer { get; set; }

    public static CardModel Create(string? title, IHtmlContent body, string? eyebrow = null, string? subtitle = null)
        => new()
        {
            Title = title,
            Eyebrow = eyebrow,
            Subtitle = subtitle,
            Body = body
        };

    public static HtmlString Html(string markup) => new(markup);
}
