using Microsoft.AspNetCore.Components;

namespace CorporatePortfolio.Services.DTO
{
    public class AppState(ResumeService resumeService)
    {
        public string ClickedSkillName => _clickedSkillName;
        public FormattedText CurrentSkillSummaryText { get; private set; } = new FormattedText("");
        public List<ExperienceData> Experiences { get; set; } = [];
        public List<ProjectData> Projects { get; set; } = [];
        public List<CompetencyData> Competencies { get; set; } = [];

        public bool AreSkillsLoaded { get; set; } = false;
        public bool AreExperiencesLoaded { get; set; } = false;
        public bool ErrorOccured { get; set; } = false;
        public bool Loading { get; set; } = false;

        public event Action? ExperiencesOnChange;
        public event Action? SkillsLoaded;
        public event Action<string>? OnSkillSelected;
        public event Action? ErrorInvoked;

        private string _clickedSkillName = string.Empty;

        private CancellationTokenSource? _mouseDebounceCancellationToken;
        public CancellationTokenSource? MouseDebounceCancellationToken
        {
            get
            {
                return _mouseDebounceCancellationToken;
            }
            set
            {
                _mouseDebounceCancellationToken = value;
            }
        }

        public async Task InitializeAsync()
        {
            Loading = true;            
        }

        public async Task SetHoveredSkillSummary(string skillName)
        {
            await Task.Delay(200);
            _mouseDebounceCancellationToken?.Cancel();
            _mouseDebounceCancellationToken = new CancellationTokenSource();
            var activeToken = _mouseDebounceCancellationToken.Token;

            try
            {
                if (!activeToken.IsCancellationRequested)
                {
                    var skill = Competencies.FirstOrDefault(c => c.Name == skillName);
                    CurrentSkillSummaryText = skill?.Summary ?? new FormattedText("");

                    NotifyStateChanged();
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        public void ClearHoveredSkillSummary()
        {
            if (!string.IsNullOrEmpty(_clickedSkillName))
            {
                // If a skill was previously clicked, revert back to its summary text when the mouse leaves
                var clickedSkill = Competencies.FirstOrDefault(c => c.Name == _clickedSkillName);
                CurrentSkillSummaryText = clickedSkill?.Summary ?? new FormattedText("");
            }
            else
            {
                // If nothing was clicked, clear the text completely
                CurrentSkillSummaryText = new FormattedText("");
            }

            NotifyStateChanged();
        }

        public void SetClickedSkillSummary(string skillName)
        {
            if (_clickedSkillName == skillName)
            {
                _clickedSkillName = string.Empty;
                CurrentSkillSummaryText = new FormattedText("");
            }
            else
            {
                _clickedSkillName = skillName;
                var skill = Competencies.FirstOrDefault(c => c.Name == skillName);
                CurrentSkillSummaryText = skill?.Summary ?? new FormattedText("");
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

        public void UpdateExperiencesLoadingState(bool isLoaded)
        {
            if (AreExperiencesLoaded != isLoaded)
            {
                AreExperiencesLoaded = isLoaded;
                NotifyStateChanged();
            }
        }        

        public void NotifyStateChanged()
        {
            ExperiencesOnChange?.Invoke();
            SkillsLoaded?.Invoke();
        }

        public void NotifyErrorStateChanged()
        {
            ErrorOccured = true;
            ErrorInvoked?.Invoke();
        }
    }
}
