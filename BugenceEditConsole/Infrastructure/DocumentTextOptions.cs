namespace BugenceEditConsole.Infrastructure;

public class DocumentTextOptions
{
    public const string DefaultTessdataUrl = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata";

    public string TessdataUrl { get; set; } = DefaultTessdataUrl;
    public string Language { get; set; } = "eng";
    public int Dpi { get; set; } = 300;
    public int MaxPages { get; set; }
}
