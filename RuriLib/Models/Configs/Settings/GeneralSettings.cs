namespace RuriLib.Models.Configs.Settings
{
    public class GeneralSettings
    {
        public bool VerboseMode { get; set; } = false;
        public int SuggestedBots { get; set; } = 1;
        public int MaximumCPM { get; set; } = 0;
        public bool SaveEmptyCaptures { get; set; } = false;
        public bool ReportLastCaptchaOnRetry { get; set; } = false;

        // Maximum number of allowed label jumps before considering it an infinite loop
        public int MaxJumpIterations { get; set; } = 40;

        public string[] ContinueStatuses { get; set; } = new string[]
        {
            "SUCCESS",
            "NONE"
        };
    }
}
