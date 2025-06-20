﻿using System;

namespace RuriLib.Logging
{
    /// <summary>
    /// An entry of a <see cref="BotLogger"/>.
    /// </summary>
    public class BotLoggerEntry
    {
        /// <summary>
        /// The logged message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The color of the message when displayed in a UI.
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// Whether the message contains HTML code and can be rendered as HTML.
        /// </summary>
        public bool CanViewAsHtml { get; set; } = false;

        /// <summary>
        /// The date and time when the entry was added.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// The exception that was logged, if any.
        /// </summary>
        public Exception? Exception { get; set; }
    }
}
