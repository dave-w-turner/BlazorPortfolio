using Microsoft.AspNetCore.Components;

namespace CorporatePortfolio.Services.DTO
{
    public class AppState(ResumeService resumeService)
    {
        // Initialize as an empty list so components don't crash while loading
        public List<ExperienceData> Experiences { get; private set; } = [];

        public event Action? ExperiencesOnChange;

        public async Task InitializeAsync()
        {
            Experiences = await resumeService.GetExperience();

            foreach (var exp in Experiences)
                foreach (var detail in exp.Details)
                    exp.DetailsFormatted.Add(new MarkupString(detail));

            NotifyStateChanged();
        }

        public void NotifyStateChanged() => ExperiencesOnChange?.Invoke();
    }
}
