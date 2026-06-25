using System;

namespace NoteKeeping
{
    public class ThemeService
    {
        public string Theme { get; set; } = "default";

        public string? LayoutValue { get; private set; }
        public event Func<Task>? OnThemeChanged;

        public async Task SetTheme(string theme)
        {
            Theme = theme;
            if (OnThemeChanged != null)
            {
                await OnThemeChanged.Invoke();
            }
        }
        public async Task SetLayoutValue(string value)
        {
            LayoutValue = value;
            await NotifyChange();
        }

        private async Task NotifyChange()
        {
            if (OnThemeChanged != null)
            {
                await OnThemeChanged.Invoke();
            }
        }

        public string BgColor => Theme switch
        {
            "living" => "#44352e",
            "study" => "#F5DEB3",
            "bedroom" => "#FFE4B5",
            "default" => "#FFFFFF",
            _ => "#FFFFFF"
        };

        public string TextColor => Theme switch
        {
            "living" => "#f5d8ae",
            "study" => "#654321",
            "bedroom" => "#A0522D",
            "default" => "#333333",
            _ => "#333333"
        };

        public string AccentColor => Theme switch
        {
            "living" => "#8a6956",
            "study" => "#FFD700",
            "bedroom" => "#FFA500",
            "default" => "#808080",
            _ => "#808080"
        };

        public string BorderColor => Theme switch
        {
            "living" => "#d5b397",
            "study" => "#B8860B",
            "bedroom" => "#CD853F",
            "default" => "#CCCCCC",
            _ => "#CCCCCC"
        };

        public string ShadowColor => Theme switch
        {
            "living" => "rgb(187, 183, 180, 0.3)",
            "study" => "rgba(255, 215, 0, 0.3)",
            "bedroom" => "rgba(255, 165, 0, 0.3)",
            "default" => "rgba(0, 0, 0, 0.1)",
            _ => "rgba(0, 0, 0, 0.1)"
        };
    }
}