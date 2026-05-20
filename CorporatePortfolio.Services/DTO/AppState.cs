using Microsoft.AspNetCore.Components;

namespace CorporatePortfolio.Services.DTO
{
    public class AppState(ResumeService resumeService)
    {
        public List<ExperienceData> Experiences { get; private set; } = [];
        public List<CompetencyData> Competencies { get; set; } = [];
        public event Action<string>? OnSkillSelected;
        public bool AreSkillsLoaded { get; set; } = false;
        public event Action? ExperiencesOnChange;
        public event Action? SkillsLoaded;

        public async Task InitializeAsync()
        {
            Experiences = await resumeService.GetExperience();

            foreach (var exp in Experiences)
                foreach (var detail in exp.Details)
                    exp.DetailsFormatted.Add(new MarkupString(detail));
        }

        public void SelectSkill(string skillName)
        {
            OnSkillSelected?.Invoke(skillName);
        }

        public void UpdateSkillsLoadingState(bool isLoaded)
        {
            if (AreSkillsLoaded != isLoaded)
            {
                AreSkillsLoaded = isLoaded;
                NotifyStateChanged();
            }
        }

        public void NotifyStateChanged()
        {
            ExperiencesOnChange?.Invoke();
            SkillsLoaded?.Invoke();
        }
    }
}
