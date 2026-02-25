using BugenceEditConsole.Models;
using FluentValidation;

namespace BugenceEditConsole.Validation;

public class SectionUpsertFormValidator : AbstractValidator<SectionUpsertForm>
{
    private static readonly string[] AllowedContentTypes = new[] { "Text", "Html", "Image", "RichText" };
    private const int MaxTextLength = 8_000;
    private const int MaxAltTextLength = 300;
    private const long MaxImageBytes = 5 * 1024 * 1024; // 5MB

    public SectionUpsertFormValidator()
    {
        RuleFor(form => form.ContentType)
            .NotEmpty().WithMessage("Content type is required.")
            .Must(IsAllowedContentType).WithMessage("Content type must be Text, Html, RichText, or Image.");

        RuleFor(form => form.Selector)
            .NotEmpty().WithMessage("A CSS selector is required when creating a new section.")
            .When(form => !form.SectionId.HasValue || form.SectionId == Guid.Empty);

        When(form => IsTextual(form.ContentType), () =>
        {
            RuleFor(form => form.ContentValue)
                .NotEmpty().WithMessage("Content value is required for text sections.")
                .MaximumLength(MaxTextLength).WithMessage($"Content value cannot exceed {MaxTextLength} characters.");
        });

        When(form => IsImage(form.ContentType), () =>
        {
            RuleFor(form => form.MediaAltText)
                .NotEmpty().WithMessage("Alt text is required for image sections.")
                .MaximumLength(MaxAltTextLength).WithMessage($"Alt text cannot exceed {MaxAltTextLength} characters.");

            RuleFor(form => form.Image)
                .Must(file => file is not null && file.Length > 0)
                .When(form => !form.SectionId.HasValue || form.SectionId == Guid.Empty)
                .WithMessage("Image file is required when creating a new image section.");

            RuleFor(form => form.Image)
                .Must(file => file is null || file.Length <= MaxImageBytes)
                .WithMessage("Image file must be 5MB or smaller.");
        });

        RuleFor(form => form.MediaAltText)
            .MaximumLength(MaxAltTextLength)
            .When(form => !IsImage(form.ContentType))
            .WithMessage($"Alt text cannot exceed {MaxAltTextLength} characters.");
    }

    private static bool IsAllowedContentType(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedContentTypes.Any(type => type.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool IsTextual(string? value) =>
        value != null && (
            value.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Html", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("RichText", StringComparison.OrdinalIgnoreCase));

    private static bool IsImage(string? value) =>
        value != null && value.Equals("Image", StringComparison.OrdinalIgnoreCase);
}
