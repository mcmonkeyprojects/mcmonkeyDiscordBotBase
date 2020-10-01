using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBotBase
{
    /// <summary>
    /// Constants (links, image urls, etc).
    /// Either absolute constants, or config-loaded pseudo-constants.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// A warning emoji image URL.
        /// </summary>
        public const string WARNING_ICON = "https://raw.githubusercontent.com/twitter/twemoji/master/assets/72x72/26a0.png";

        /// <summary>
        /// Generic reusable "information" icon.
        /// </summary>
        public const string INFO_ICON = "https://raw.githubusercontent.com/twitter/twemoji/master/assets/72x72/2139.png";

        /// <summary>
        /// Generic reusable "speech bubble" icon.
        /// </summary>
        public const string SPEECH_BUBBLE_ICON = "https://raw.githubusercontent.com/twitter/twemoji/master/assets/72x72/1f4ac.png";

        /// <summary>
        /// Generic reusable "red flag" icon.
        /// </summary>
        public const string RED_FLAG_ICON = "https://raw.githubusercontent.com/twitter/twemoji/master/assets/72x72/1f6a9.png";

        /// <summary>
        /// A checkmark "accept"/"yes" emoji, for reactions.
        /// </summary>
        public const string ACCEPT_EMOJI = "☑️";

        /// <summary>
        /// A Red-X "deny"/"no" emoji, for reactions.
        /// </summary>
        public const string DENY_EMOJI = "❌";
    }
}
