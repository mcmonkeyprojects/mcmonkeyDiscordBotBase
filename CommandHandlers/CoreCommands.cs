using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Discord;

namespace DiscordBotBase.CommandHandlers
{
    /// <summary>
    /// Commands that most bots need.
    /// </summary>
    public class CoreCommands : UserCommands
    {
        /// <summary>
        /// Constructs the core commands helper.
        /// </summary>
        public CoreCommands(Func<IUser, bool> isUserAdmin)
        {
            UserAdminCheckMethod = isUserAdmin;
        }

        /// <summary>
        /// Method to check if the user is an admin.
        /// </summary>
        public Func<IUser, bool> UserAdminCheckMethod;

        /// <summary>
        /// Bot restart admin command.
        /// </summary>
        public void CMD_Restart(string[] cmds, IUserMessage message)
        {
            if (!UserAdminCheckMethod(message.Author))
            {
                SendErrorMessageReply(message, "Authorization Failure", "Nope! That's not for you!");
                return;
            }
            if (!File.Exists("./start.sh"))
            {
                SendErrorMessageReply(message, "Cannot Comply", "Nope! That's not valid for my current configuration! (`start.sh` missing).");
            }
            SendGenericPositiveMessageReply(message, "Restarting", "Yes, boss. Restarting now...");
            Process.Start("bash", "./start.sh " + message.Channel.Id);
            Task.Factory.StartNew(() =>
            {
                Console.WriteLine("Shutdown start...");
                for (int i = 0; i < 15; i++)
                {
                    Console.WriteLine("T Minus " + (15 - i));
                    Task.Delay(1000).Wait();
                }
                Console.WriteLine("Shutdown!");
                Environment.Exit(0);
            });
            Bot.BotMonitor.StopAllLogic = true;
            Bot.Shutdown();
        }
    }
}
