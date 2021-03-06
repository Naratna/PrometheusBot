using Discord;
using Discord.Commands;
using PrometheusBot.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Victoria;
using Victoria.Enums;
using Victoria.EventArgs;
using Victoria.Responses.Search;

namespace PrometheusBot.Services.Music
{
    public class MusicService
    {
        private LavaNode _lavaNode;

        public MusicService(LavaNode lavaNode)
        {
            _lavaNode = lavaNode;
            _lavaNode.OnTrackEnded += OnTrackEndedAsync;
        }

        public static async Task OnTrackEndedAsync(TrackEndedEventArgs args)
        {
            var player = args.Player;
            if (player.Queue is null) return;
            var hasNext = player.Queue.TryDequeue(out var nextTrack);
            if (!hasNext) return;
            await player.PlayAsync(nextTrack);
        }

        public async Task<CommandResult> JoinAsync(SocketCommandContext context)
        {
            if (_lavaNode.HasPlayer(context.Guild))
                return CommandResult.FromError(CommandError.Unsuccessful, "Already connected to a voice channel");

            var voiceState = context.User as IVoiceState;
            if (voiceState?.VoiceChannel is null)
                return CommandResult.FromError(CommandError.Unsuccessful, "You must be connected to a voice channel");

            await _lavaNode.JoinAsync(voiceState.VoiceChannel, context.Channel as ITextChannel);
            await context.Channel.SendMessageAsync($"Joined {voiceState.VoiceChannel.Name}");
            return CommandResult.FromSuccess();
        }

        public async Task<CommandResult> PlayAsync(SocketCommandContext context, string query)
        {
            if (!_lavaNode.HasPlayer(context.Guild))
                await JoinAsync(context);

            var player = _lavaNode.GetPlayer(context.Guild);

            var type = Uri.IsWellFormedUriString(query, UriKind.Absolute) ? SearchType.Direct : SearchType.YouTube;
            var reply = context.Channel.SendMessageAsync($"Searching for `{query}...`");
            var search = await _lavaNode.SearchAsync(type, query);
            await reply;


            if (search.Status == SearchStatus.NoMatches)
                return CommandResult.FromError(CommandError.Unsuccessful, "No matches found");

            if (search.Status == SearchStatus.LoadFailed)
                return CommandResult.FromError(CommandError.Unsuccessful, "Unable to load requested song");

            if (search.Status == SearchStatus.PlaylistLoaded)
            {
                await PlayPlaylistAsync(context, player, search);
                return CommandResult.FromSuccess();
            }

            await PlaySongAsync(context, player, search.Tracks.First());
            return CommandResult.FromSuccess();
        }

        public async Task<CommandResult> PauseAsync(SocketCommandContext context)
        {
            bool hasPlayer = _lavaNode.TryGetPlayer(context.Guild, out var player);
            if (!hasPlayer)
                return CommandResult.FromError(CommandError.Unsuccessful, "Bot must be in a voice channel in order to use this command");

            if (player.PlayerState == PlayerState.Stopped)
                return CommandResult.FromError(CommandError.Unsuccessful, "Nothing is currently playing");

            if (player.PlayerState == PlayerState.Paused)
                return CommandResult.FromError(CommandError.Unsuccessful, "Playback already paused");

            await player.PauseAsync();
            await context.Channel.SendMessageAsync("Playback paused, use command `resume` to un-pause it");
            return CommandResult.FromSuccess();
        }

        public async Task<CommandResult> ResumeAsync(SocketCommandContext context)
        {
            bool hasPlayer = _lavaNode.TryGetPlayer(context.Guild, out var player);
            if (!hasPlayer)
                return CommandResult.FromError(CommandError.Unsuccessful, "Bot must be in a voice channel in order to use this command");

            if (player.PlayerState == PlayerState.Stopped)
                return CommandResult.FromError(CommandError.Unsuccessful, "Nothing is currently playing");

            if (player.PlayerState == PlayerState.Playing)
                return CommandResult.FromError(CommandError.Unsuccessful, "Playback already resumed");

            await player.ResumeAsync();
            await context.Channel.SendMessageAsync("Playback resumed");
            return CommandResult.FromSuccess();
        }

        public async Task<CommandResult> ForceSkipAsync(SocketCommandContext context)
        {
            bool hasPlayer = _lavaNode.TryGetPlayer(context.Guild, out var player);
            if (!hasPlayer)
                return CommandResult.FromError(CommandError.Unsuccessful, "Bot must be in a voice channel in order to use this command");

            var track = player.Track;

            if (track is null)
                return CommandResult.FromError(CommandError.Unsuccessful, "Nothing to skip");

            await player.StopAsync();
            await context.Channel.SendMessageAsync($"Skipped `{track.Title}`");
            return CommandResult.FromSuccess();
        }

        public async Task<CommandResult> ShuffleAsync(SocketCommandContext context)
        {
            bool hasPlayer = _lavaNode.TryGetPlayer(context.Guild, out var player);
            if (!hasPlayer)
                return CommandResult.FromError(CommandError.Unsuccessful, "Bot must be in a voice channel in order to use this command");

            player.Queue.Shuffle();
            await context.Channel.SendMessageAsync("Shuffled the queue");
            return CommandResult.FromSuccess();
        }

        public async Task<CommandResult> DisconnectAsync(SocketCommandContext context)
        {
            bool hasPlayer = _lavaNode.TryGetPlayer(context.Guild, out var player);
            if (!hasPlayer)
                return CommandResult.FromError(CommandError.Unsuccessful, "Bot not currently in any voice channel");

            await _lavaNode.LeaveAsync(player.VoiceChannel);
            await context.Channel.SendMessageAsync("Left the voice channel");
            return CommandResult.FromSuccess();
        }

        private static async Task PlayPlaylistAsync(SocketCommandContext context, LavaPlayer player, SearchResponse search)
        {
            if (player.Track is not null || player.PlayerState == Victoria.Enums.PlayerState.Paused)
            {
                Console.WriteLine(search.Tracks.Count);
                player.Queue.Enqueue(search.Tracks);
                await context.Channel.SendMessageAsync($"Playlist `{search.Playlist.Name}` Added to queue");
            }
            var first = search.Tracks.First();
            player.Queue.Enqueue(search.Tracks.Skip(1));
            _ = context.Channel.SendMessageAsync($"Now playing `{first.Title}`\nThe rest of the playlist was added to the queue");
            await player.PlayAsync(first);
        }

        private static async Task PlaySongAsync(SocketCommandContext context, LavaPlayer player, LavaTrack track)
        {
            if (player.Track is not null || player.PlayerState == Victoria.Enums.PlayerState.Paused)
            {
                player.Queue.Enqueue(track);
                await context.Channel.SendMessageAsync($"`{track.Title}` added to queue");
                return;
            }
            _ = context.Channel.SendMessageAsync($"Now playing `{track.Title}`");
            await player.PlayAsync(track);
        }
    }
}
