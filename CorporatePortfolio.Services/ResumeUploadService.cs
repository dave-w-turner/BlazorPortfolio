using Microsoft.AspNetCore.Components.Forms;

namespace CorporatePortfolio.Services
{
    public class ResumeUploadService(string webRootPath, string contentRootPath)
    {
        private const long MaxFileSizeInBytes = 1024 * 1024 * 5;

        public async Task<(bool Success, string Message)> UploadResumeAsync(IBrowserFile file)
        {
            if (file == null || file.Size == 0)
                return (false, "No file was uploaded.");

            if (file.Size > MaxFileSizeInBytes)
                return (false, "File size exceeds the maximum limit of 5MB.");

            var fileExtension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (fileExtension != ".docx")
                return (false, "Invalid file type. Only .docx files are permitted.");

            try
            {
                var uploadFolder = webRootPath;

                if (string.IsNullOrEmpty(uploadFolder))
                {
                    uploadFolder = Path.Combine(contentRootPath, "wwwroot");
                }

                if (uploadFolder.Contains("wwwroot\\wwwroot"))
                {
                    uploadFolder = uploadFolder.Replace("wwwroot\\wwwroot", "wwwroot");
                }

                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                var uniqueFileName = "DavidTurner_Resume.docx";
                var filePath = Path.Combine(uploadFolder, uniqueFileName);

                using var sourceStream = file.OpenReadStream(MaxFileSizeInBytes);
                using var targetStream = new FileStream(filePath, FileMode.Create);

                await sourceStream.CopyToAsync(targetStream);

                return (true, "Resume uploaded successfully!");
            }
            catch (Exception ex)
            {
                return (false, $"Internal server error: {ex.Message}");
            }
        }
    }
}
