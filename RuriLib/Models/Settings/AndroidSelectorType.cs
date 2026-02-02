namespace RuriLib.Models.Settings
{
    /// <summary>
    /// Types of selectors for finding Android UI elements.
    /// </summary>
    public enum AndroidSelectorType
    {
        /// <summary>
        /// Find by resource-id (e.g., "com.app:id/button").
        /// </summary>
        Id,

        /// <summary>
        /// Find by XPath expression.
        /// </summary>
        XPath,

        /// <summary>
        /// Find by accessibility ID (content-desc attribute).
        /// </summary>
        AccessibilityId,

        /// <summary>
        /// Find by class name (e.g., "android.widget.Button").
        /// </summary>
        ClassName,

        /// <summary>
        /// Find by exact text content.
        /// </summary>
        Text,

        /// <summary>
        /// Find by partial text content.
        /// </summary>
        PartialText,

        /// <summary>
        /// Find using UiAutomator2 selector string.
        /// </summary>
        UiAutomator
    }
}
