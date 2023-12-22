using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace DiscordBotBase
{
    /// <summary>General program entry and handler.</summary>
    public class DiscordBotBaseHelper
    {
        /// <summary>The current bot object (the instance will change if the bot is restarted).</summary>
        public static DiscordBot CurrentBot = null;

        /// <summary>Software entry point - starts the bot.</summary>
        public static void StartBotHandler(string[] args, DiscordBotConfig internalConfig)
        {
            LaunchBotThread(args, internalConfig);
        }

        /// <summary>Launches a bot thread.</summary>
        public static void LaunchBotThread(string[] args, DiscordBotConfig internalConfig)
        {
            CurrentBot = new DiscordBot
            {
                ClientConfig = internalConfig
            };
            Thread thr = new(new ParameterizedThreadStart(BotThread)) { Name = "discordbotthread" };
            thr.Start(args);
        }

        /// <summary>The bot thread rootmost method, takes a string array object as input.</summary>
        public static void BotThread(Object obj)
        {
            try
            {
                CurrentBot.InitAndRun(obj as string[]);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Discord crash: " + ex.ToString());
                Thread.Sleep(10 * 1000);
                LaunchBotThread([], CurrentBot.ClientConfig);
            }
        }
    }
}
