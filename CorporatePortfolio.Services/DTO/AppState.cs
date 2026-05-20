using Microsoft.AspNetCore.Components;

namespace CorporatePortfolio.Services.DTO
{
    public class AppState(ResumeService resumeService)
    {
        public string ClickedSkillName => _clickedSkillName;
        public string CurrentSkillSummaryText { get; private set; } = string.Empty;
        public List<ExperienceData> Experiences { get; private set; } = [];
        public List<CompetencyData> Competencies { get; set; } = [];
        public event Action<string>? OnSkillSelected;
        public bool AreSkillsLoaded { get; set; } = false;
        public event Action? ExperiencesOnChange;
        public event Action? SkillsLoaded;

        private string _clickedSkillName = string.Empty;

        public async Task InitializeAsync()
        {
            Experiences = await resumeService.GetExperience();

            foreach (var exp in Experiences)
                foreach (var detail in exp.Details)
                    exp.DetailsFormatted.Add(new MarkupString(detail));
        }

        public void SetHoveredSkillSummary(string skillName)
        {
            var skill = Competencies.FirstOrDefault(c => c.Name == skillName);
            CurrentSkillSummaryText = skill?.Summary ?? string.Empty;
            NotifyStateChanged();
        }

        public void ClearHoveredSkillSummary()
        {
            if (!string.IsNullOrEmpty(_clickedSkillName))
            {
                // If a skill was previously clicked, revert back to its summary text when the mouse leaves
                var clickedSkill = Competencies.FirstOrDefault(c => c.Name == _clickedSkillName);
                CurrentSkillSummaryText = clickedSkill?.Summary ?? string.Empty;
            }
            else
            {
                // If nothing was clicked, clear the text completely
                CurrentSkillSummaryText = string.Empty;
            }

            NotifyStateChanged();
        }

        public void SetClickedSkillSummary(string skillName)
        {
            if (_clickedSkillName == skillName)
            {
                // Toggle off: Clicking the already active card unlocks it
                _clickedSkillName = string.Empty;
                CurrentSkillSummaryText = string.Empty;
            }
            else
            {
                // Lock down the summary for this new skill
                _clickedSkillName = skillName;
                var skill = Competencies.FirstOrDefault(c => c.Name == skillName);
                CurrentSkillSummaryText = skill?.Summary ?? string.Empty;
            }

            NotifyStateChanged();
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
