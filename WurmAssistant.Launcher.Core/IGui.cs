using System;
using System.Drawing;

namespace AldursLab.WurmAssistant.Launcher.Core
{
    public interface IGui : IProgressReporter
    {
        void ShowGui();

        void AddUserMessage(string message, Color? textColor = null);

        void HideGui();

        void SetState(LauncherState state);

        void EnableSkip();

        void DisableSkip();

        Action SkipAction { get; set; }
    }

    public enum LauncherState
    {
        Running,
        Error
    }
}