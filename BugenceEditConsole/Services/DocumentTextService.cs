using System.Drawing;
using System.Text;
using System.Drawing.Imaging;
using BugenceEditConsole.Infrastructure;
using PdfiumViewer;
using Tesseract;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public record DocumentTextSaveRequest(string? FileName, string? SourceType, int? PageCount, string? Text);
public record DocumentTextSaveResult(Guid Id, string FileName, string StoredName, DateTime SavedAtUtc);
public record DocumentTextOcrResult(string Text, int Pages);

public interface IDocumentTextService
{
    Task<DocumentTextSaveResult> SaveAsync(DocumentTextSaveRequest request, CancellationToken cancellationToken = default);
    Task<DocumentTextOcrResult> OcrPdfAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}

public class DocumentTextService : IDocumentTextService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DocumentTextOptions _options;
    private readonly ILogger<DocumentTextService> _logger;

    public DocumentTextService(
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        IOptions<DocumentTextOptions> options,
        ILogger<DocumentTextService> logger)
    {
        _environment = environment;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentTextSaveResult> SaveAsync(DocumentTextSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new InvalidOperationException("Text is required.");
        }

        var root = Path.Combine(_environment.ContentRootPath, "App_Data", "document-text");
        Directory.CreateDirectory(root);

        var baseName = SlugGenerator.Slugify(Path.GetFileNameWithoutExtension(request.FileName) ?? "document");
        var id = Guid.NewGuid();
        var savedAt = DateTime.UtcNow;
        var storedName = $"{baseName}-{savedAt:yyyyMMddHHmmss}-{id:N}.json";
        var path = Path.Combine(root, storedName);

        var payload = new DocumentTextStoredPayload
        {
            Id = id,
            FileName = request.FileName ?? baseName,
            SourceType = request.SourceType,
            PageCount = request.PageCount,
            Text = request.Text,
            SavedAtUtc = savedAt
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json, cancellationToken);

        return new DocumentTextSaveResult(id, payload.FileName, storedName, savedAt);
    }

    public async Task<DocumentTextOcrResult> OcrPdfAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        var tessdataPath = await EnsureTessdataAsync(cancellationToken);
        var pageLimit = _options.MaxPages <= 0 ? int.MaxValue : _options.MaxPages;
        var dpi = _options.Dpi <= 0 ? 300 : _options.Dpi;

        using var document = PdfDocument.Load(pdfStream);
        var totalPages = document.PageCount;
        var pagesToProcess = Math.Min(totalPages, pageLimit);

        using var engine = new TesseractEngine(tessdataPath, _options.Language, EngineMode.LstmOnly);
        var builder = new StringBuilder();

        for (var i = 0; i < pagesToProcess; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = document.Render(i, dpi, dpi, true);
            using var bitmap = image as Bitmap ?? new Bitmap(image);
            using var pix = LoadPixFromBitmap(bitmap);
            using var page = engine.Process(pix);
            var text = page.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.AppendLine(text.Trim());
                builder.AppendLine();
            }
        }

        return new DocumentTextOcrResult(builder.ToString().Trim(), totalPages);
    }

    private async Task<string> EnsureTessdataAsync(CancellationToken cancellationToken)
    {
        var tessRoot = Path.Combine(_environment.ContentRootPath, "App_Data", "tessdata");
        Directory.CreateDirectory(tessRoot);

        var lang = string.IsNullOrWhiteSpace(_options.Language) ? "eng" : _options.Language;
        var trainedFile = Path.Combine(tessRoot, $"{lang}.traineddata");
        if (File.Exists(trainedFile))
        {
            return tessRoot;
        }

        var url = string.IsNullOrWhiteSpace(_options.TessdataUrl)
            ? DocumentTextOptions.DefaultTessdataUrl
            : _options.TessdataUrl;

        var client = _httpClientFactory.CreateClient("document-text");
        _logger.LogInformation("Downloading tessdata for OCR from {Url}", url);
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(trainedFile);
        await response.Content.CopyToAsync(output, cancellationToken);

        return tessRoot;
    }

    private sealed class DocumentTextStoredPayload
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? SourceType { get; set; }
        public int? PageCount { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime SavedAtUtc { get; set; }
    }

    private static Pix LoadPixFromBitmap(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        return Pix.LoadFromMemory(memory.ToArray());
    }
}
