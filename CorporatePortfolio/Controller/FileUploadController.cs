using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporatePortfolio.Controller;

[ApiController]
[Route("[controller]")]
[Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme, Roles = "Admin")]
public class FileUploadController(IWebHostEnvironment environment) : ControllerBase
{
    private readonly IWebHostEnvironment _environment = environment;
    private const long MaxFileSizeInBytes = 1024 * 1024 * 5;

    [HttpPost("upload-resume")]
    public async Task<IActionResult> UploadResume([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file was uploaded.");

        if (file.Length > MaxFileSizeInBytes)
            return BadRequest("File size exceeds the maximum limit of 5MB.");

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (fileExtension != ".docx")
            return BadRequest("Invalid file type. Only .docx files are permitted.");

        try
        {
            var uploadFolder = _environment.WebRootPath;
            var uniqueFileName = "DavidTurner_Resume.docx";
            var filePath = Path.Combine(uploadFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            return Ok(new { message = "Resume uploaded successfully!", fileName = uniqueFileName });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
