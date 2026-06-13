using Microsoft.AspNetCore.Mvc;
using SkillForge.API.Services;
using SkillForge.DTOs;
using System.Text.Json;

namespace SkillForge.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CandidateController : ControllerBase
{
    private readonly AIService _aiService;
    private readonly ILogger<CandidateController> _logger;

    public CandidateController(AIService aiService, ILogger<CandidateController> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    [HttpPost("analyze-resume")]
    public async Task<IActionResult> AnalyzeResume(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded" });

            var allowedExtensions = new[] { ".pdf", ".docx", ".txt" };
            var fileExtension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(fileExtension))
                return BadRequest(new { error = "Only PDF, DOCX, TXT allowed" });

            string fileContent;
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;

                fileContent = fileExtension switch
                {
                    ".pdf" => ExtractTextFromPdf(stream),
                    ".docx" => ExtractTextFromDocx(stream),
                    ".txt" => ExtractTextFromTxt(stream),
                    _ => throw new InvalidOperationException("Unsupported file type")
                };
            }

            if (string.IsNullOrWhiteSpace(fileContent))
                return BadRequest(new { error = "Could not extract text from file" });

            _logger.LogInformation("Resume text extracted: {Length} characters", fileContent.Length);

            // Call AI Service to analyze
            var result = await _aiService.AnalyzeResumeAsync(fileContent);

            if (result == null)
                return StatusCode(500, new { error = "Analysis failed" });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing resume");
            return StatusCode(500, new { error = "Processing error", details = ex.Message });
        }
    }

    private string ExtractTextFromPdf(MemoryStream stream)
    {
        try
        {
            var document = UglyToad.PdfPig.PdfDocument.Open(stream);
            var text = string.Join("\n", document.GetPages().Select(p => p.Text));
            _logger.LogInformation("PDF extracted: {Length} characters", text.Length);
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF extraction failed");
            throw;
        }
    }

    private string ExtractTextFromDocx(MemoryStream stream)
    {
        try
        {
            var text = new System.Text.StringBuilder();
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read))
            {
                var xmlPart = archive.Entries.FirstOrDefault(e => e.FullName == "word/document.xml");
                if (xmlPart == null)
                    throw new InvalidOperationException("Invalid DOCX file");

                using (var entryStream = xmlPart.Open())
                using (var reader = new StreamReader(entryStream))
                {
                    var xmlContent = reader.ReadToEnd();
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(xmlContent);

                    var textNodes = doc.GetElementsByTagName("w:t");
                    foreach (System.Xml.XmlElement node in textNodes)
                        text.Append(node.InnerText);
                }
            }
            var result = text.ToString();
            _logger.LogInformation("DOCX extracted: {Length} characters", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DOCX extraction failed");
            throw;
        }
    }

    private string ExtractTextFromTxt(MemoryStream stream)
    {
        try
        {
            stream.Position = 0;
            using (var reader = new StreamReader(stream))
            {
                var text = reader.ReadToEnd();
                _logger.LogInformation("TXT extracted: {Length} characters", text.Length);
                return text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TXT extraction failed");
            throw;
        }
    }

    [HttpGet("{id}")]
    public IActionResult GetCandidate(int id)
    {
        return Ok(new { message = "Placeholder" });
    }
}