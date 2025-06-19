using System.Windows.Input;

namespace OpenBullet2.Native
{
    public static class CustomCommands
    {
        public static readonly RoutedUICommand NewConfig = new RoutedUICommand
            (
                "New Config",
                "NewConfig",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.N, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand OpenConfig = new RoutedUICommand
            (
                "Open Config",
                "OpenConfig",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.O, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand SaveConfig = new RoutedUICommand
            (
                "Save Config",
                "SaveConfig",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.S, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand Refresh = new RoutedUICommand
            (
                "Refresh",
                "Refresh",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.F5, ModifierKeys.None)
                }
            );

        public static readonly RoutedUICommand Quit = new RoutedUICommand
            (
                "Quit",
                "Quit",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.Q, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToHome = new RoutedUICommand
            (
                "Navigate to Home",
                "NavigateToHome",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D1, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToJobs = new RoutedUICommand
            (
                "Navigate to Jobs",
                "NavigateToJobs",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D2, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToMonitor = new RoutedUICommand
            (
                "Navigate to Monitor",
                "NavigateToMonitor",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D3, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToProxies = new RoutedUICommand
            (
                "Navigate to Proxies",
                "NavigateToProxies",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D4, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToWordlists = new RoutedUICommand
            (
                "Navigate to Wordlists",
                "NavigateToWordlists",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D5, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToConfigs = new RoutedUICommand
            (
                "Navigate to Configs",
                "NavigateToConfigs",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D6, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToHits = new RoutedUICommand
            (
                "Navigate to Hits",
                "NavigateToHits",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D7, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToPlugins = new RoutedUICommand
            (
                "Navigate to Plugins",
                "NavigateToPlugins",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D8, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToOBSettings = new RoutedUICommand
            (
                "Navigate to OB Settings",
                "NavigateToOBSettings",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D9, ModifierKeys.Control)
                }
            );

        public static readonly RoutedUICommand NavigateToRLSettings = new RoutedUICommand
            (
                "Navigate to RL Settings",
                "NavigateToRLSettings",
                typeof(CustomCommands),
                new InputGestureCollection()
                {
                    new KeyGesture(Key.D0, ModifierKeys.Control)
                }
            );
    }
} 