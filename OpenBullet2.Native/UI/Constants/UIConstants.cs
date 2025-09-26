namespace OpenBullet2.Native.UI.Constants
{
    /// <summary>
    /// Central location for UI-related constants and configuration values
    /// </summary>
    public static class UIConstants
    {
        /// <summary>
        /// Layout and spacing constants
        /// </summary>
        public static class Layout
        {
            public const double DefaultMargin = 8.0;
            public const double LargeMargin = 16.0;
            public const double DefaultPadding = 12.0;
            public const double DefaultBorderRadius = 8.0;
            public const double DefaultIconSize = 16.0;
            public const double LargeIconSize = 24.0;
        }

        /// <summary>
        /// Window size constants for application windows
        /// </summary>
        public static class WindowSizes
        {
            public const double SidebarWidth = 260.0;
            public const double MinimumWindowWidth = 800.0;
            public const double MinimumWindowHeight = 600.0;
        }

        /// <summary>
        /// Resource key constants for theme resources already used in the app
        /// </summary>
        public static class ResourceKeys
        {
            // Theme colors (from ModernTheme.xaml)
            public const string BackgroundMain = "Modern.BackgroundMain";
            public const string BackgroundSecondary = "Modern.BackgroundSecondary";
            public const string BackgroundCard = "Modern.BackgroundCard";
            public const string BorderMain = "Modern.BorderMain";
            
            // Button styles (already defined in the app)
            public const string ModernButton = "ModernButton";
            public const string ModernNavButton = "ModernNavButton";
            public const string ModernWarningButton = "ModernWarningButton";
        }
    }
}
