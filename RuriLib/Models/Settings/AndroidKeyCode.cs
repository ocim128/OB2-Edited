namespace RuriLib.Models.Settings
{
    /// <summary>
    /// Android key codes for hardware button simulation.
    /// Based on Android KeyEvent constants.
    /// </summary>
    public enum AndroidKeyCode
    {
        /// <summary>Back button.</summary>
        Back = 4,

        /// <summary>Home button.</summary>
        Home = 3,

        /// <summary>Menu/App switch button.</summary>
        Menu = 82,

        /// <summary>Recent apps button.</summary>
        RecentApps = 187,

        /// <summary>Volume up button.</summary>
        VolumeUp = 24,

        /// <summary>Volume down button.</summary>
        VolumeDown = 25,

        /// <summary>Volume mute button.</summary>
        VolumeMute = 164,

        /// <summary>Power button.</summary>
        Power = 26,

        /// <summary>Enter/Return key.</summary>
        Enter = 66,

        /// <summary>Delete/Backspace key.</summary>
        Delete = 67,

        /// <summary>Tab key.</summary>
        Tab = 61,

        /// <summary>Space key.</summary>
        Space = 62,

        /// <summary>Escape key.</summary>
        Escape = 111,

        /// <summary>Search key.</summary>
        Search = 84,

        /// <summary>Camera button.</summary>
        Camera = 27,

        /// <summary>Call/Answer button.</summary>
        Call = 5,

        /// <summary>End call button.</summary>
        EndCall = 6,

        /// <summary>Dpad Up.</summary>
        DpadUp = 19,

        /// <summary>Dpad Down.</summary>
        DpadDown = 20,

        /// <summary>Dpad Left.</summary>
        DpadLeft = 21,

        /// <summary>Dpad Right.</summary>
        DpadRight = 22,

        /// <summary>Dpad Center/OK.</summary>
        DpadCenter = 23
    }
}
