using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordBotBase
{
    /// <summary>Helper for caching Discord messages.</summary>
    public class DiscordMessageCache
    {
        /// <summary>How many messages to store per text channel.</summary>
        public int MessagesPerChannel;

        /// <summary>The backing Discord bot.</summary>
        public DiscordBot Bot;

        /// <summary>Construct the cache instance.</summary>
        public DiscordMessageCache(DiscordBot _bot, int size)
        {
            Bot = _bot;
            MessagesPerChannel = size;
        }

        /// <summary>Represents a single cached message.</summary>
        public struct CachedMessage
        {
            /// <summary>The ID of the sender of the message.</summary>
            public ulong SenderID;

            /// <summary>The text content of the sent message.</summary>
            public string Text;

            /// <summary>Attachment URLs, newline separated, if any.</summary>
            public string Attachments;
        }

        /// <summary>The caching data for a single channel.</summary>
        public class SingleChannelCache
        {
            /// <summary>Constructs the channel cache instance with a specified cache size.</summary>
            public SingleChannelCache(int _size)
            {
                Size = _size;
                IDsInSentOrder = new Queue<ulong>(Size + 32);
                Cache = new Dictionary<ulong, CachedMessage>(Size * 2);

            }

            /// <summary>All cached message IDs, in order sent, for cache clearing.</summary>
            public Queue<ulong> IDsInSentOrder;

            /// <summary>A map of message IDs to cached data.</summary>
            public Dictionary<ulong, CachedMessage> Cache;

            /// <summary>How big the cache should be.</summary>
            public int Size;

            /// <summary>Adds a new message into this channel cache.</summary>
            public void AddToCache(IMessage message)
            {
                if (Size <= 0)
                {
                    return;
                }
                lock (this)
                {
                    if (IDsInSentOrder.Count >= Size)
                    {
                        ulong id = IDsInSentOrder.Dequeue();
                        Cache.Remove(id);
                    }
                    if (!Cache.ContainsKey(message.Id))
                    {
                        IDsInSentOrder.Enqueue(message.Id);
                    }
                    Cache[message.Id] = new CachedMessage()
                    {
                        SenderID = message.Author.Id,
                        Text = message.Content,
                        Attachments = string.Join("\n", message.Attachments.Select(a => a.Url))
                    };
                }
            }
        }

        /// <summary>
        /// Tries to get the cached message for a channel ID and message ID.
        /// Returns true if the message is found, false if it's uncached.
        /// </summary>
        public bool TryGetCache(ulong channelId, ulong messageId, out CachedMessage message)
        {
            return GetCacheForChannel(channelId).Cache.TryGetValue(messageId, out message);
        }

        /// <summary>A mapping of channel IDs to channel message caches.</summary>
        public ConcurrentDictionary<ulong, SingleChannelCache> ChannelCaches = new ConcurrentDictionary<ulong, SingleChannelCache>();

        /// <summary>Gets the cache handler for a specific channel.</summary>
        public SingleChannelCache GetCacheForChannel(ulong channelID)
        {
            return ChannelCaches.GetOrAdd(channelID, (id) => new SingleChannelCache(MessagesPerChannel));
        }

        /// <summary>Caches a message.</summary>
        public void CacheMessage(IMessage message)
        {
            GetCacheForChannel(message.Channel.Id).AddToCache(message);
        }

        /// <summary>Prefill the cache with previously sent messages.</summary>
        public void Prefill()
        {
            if (MessagesPerChannel <= 0)
            {
                return;
            }
            if (MessagesPerChannel < 150)
            {
                Console.WriteLine("Performing initial cache pre-fill pass");
                PrefillInternal(50);
            }
            Console.WriteLine("Performing full cache pre-fill");
            PrefillInternal(MessagesPerChannel);
            Console.WriteLine("Cache pre-fill complete.");
        }

        /// <summary>Internal prefill call, prefer to use <see cref="Prefill"/>.</summary>
        public void PrefillInternal(int amountToFill)
        {
            foreach (SocketGuild guild in Bot.Client.Guilds)
            {
                foreach (SocketTextChannel channel in guild.TextChannels)
                {
                    if (Bot.BotMonitor.ShouldStopAllLogic())
                    {
                        return;
                    }
                    try
                    {
                        SingleChannelCache cache = GetCacheForChannel(channel.Id);
                        List<IMessage> messages = new List<IMessage>();
                        channel.GetMessagesAsync(amountToFill).ForEachAwaitAsync(async col =>
                        {
                            messages.AddRange(col);
                            await Task.Delay(100);
                        }).Wait();
                        foreach (IMessage message in messages.OrderBy(m => m.Timestamp))
                        {
                            cache.AddToCache(message);
                        }
                        Task.Delay(100).Wait();
                        Console.WriteLine($"Completed cache prefill for channel {channel.Id} ({channel.Name}) with {cache.Cache.Count} messages stored");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("error 50001: Missing Access"))
                        {
                            Console.WriteLine($"Error while prefilling cache in guild {guild.Id} ({guild.Name}) in channel {channel.Id} ({channel.Name}): no message access.");
                        }
                        else
                        {
                            Console.WriteLine($"Error while prefilling cache in guild {guild.Id} ({guild.Name}) in channel {channel.Id} ({channel.Name}): {ex}");
                        }
                    }
                }
                Console.WriteLine($"Completed cache prefill for guild {guild.Id} ({guild.Name})");
            }
        }
    }
}
