﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using PrometheusBot.Model;
using PrometheusBot.Model.Settings;

namespace PrometheusBot.Modules.Config
{
    public class ConfigModule : ModuleBase<SocketCommandContext>
    {
        [Group("configure")]
        [Alias("config", "settings")]
        public class Configuration : ModuleBase<SocketCommandContext>
        {
            static internal readonly PrometheusModel model = PrometheusModel.Instance;
            static internal readonly Dictionary<string, List<string>> settingsGroups;

            static Configuration()
            {
                settingsGroups = new Dictionary<string, List<string>>();
            }
            [Command("")]
            public async Task Configure(string scope, string setting, string action, IChannel channel = null)
                => await GenericConfigure(scope, setting, action, channel);
            [Command("")]
            public async Task Configure(string scope, string setting, string action, string value, IChannel channel = null)
                => await GenericConfigure(scope, setting, action, value, channel);
            [Command("")]
            public async Task Configure(string scope, string setting, string action, string value, string level = null)
                => await GenericConfigure(scope, setting, action, value, level);
            private async Task GenericConfigure(string scope, string setting, string action, object arg1, object arg2 = default)
            {
                if (!(scope == "user" || scope == "server"))
                {
                    await ReplyAsync($"Unknown value `{scope}`, must be one of `user` or `server`");
                    return;
                }
                SettingModel settingModel = model.GetSettingInfo(setting);
                if (settingModel is null || !settingModel.Visible)
                {
                    await base.ReplyAsync($"Unknown setting `{setting}`");
                    return;
                }
                if (scope == "user" && settingModel.Scope == SettingScope.Guild)
                {
                    await ReplyAsync($"Setting `{setting}` is a server-only setting");
                    return;
                }
                else if (scope == "server" && settingModel.Scope == SettingScope.User)
                {
                    await ReplyAsync($"Setting `{setting}` is a user-only setting");
                    return;
                }
                if (scope == "server" && !await VerifyPermissions(settingModel))
                {
                    return;
                }
                string value = null;
                object level;
                if (action == "set")
                {
                    value = arg1 as string;
                    level = arg2;
                }
                else if (action == "get" || action == "reset")
                {
                    level = arg1;
                }
                else
                {
                    await ReplyAsync($"Unknown value `{action}`, must be one of either `set`, `reset`, or `get`");
                    return;
                }
                if (scope == "user" && !(level is null || (level is string levelString && levelString.ToLower() == "global")))
                {
                    await ReplyAsync($"Unsupported value `{level}`, field must be either `global` or empty");
                    return;
                }
                if (scope == "server" && !(level is null || level is IChannel))
                {
                    await ReplyAsync($"Unsupported value `{level}`, field must be either a channel or empty");
                    return;
                }
                object objValue = null;
                if (action == "set")
                {
                    if (!settingModel.IsValid(value))
                    {
                        await ReplyAsync(settingModel.GetErrorMessage(value));
                        return;
                    }
                    try
                    {
                        objValue = settingModel.Convert(value);
                    }
                    catch (Exception)
                    {
                        await ReplyAsync(settingModel.GetErrorMessage(value));
                        return;
                    }
                }

                ulong? UId = null;
                ulong? GId = null;
                ulong? Cid = null;

                if (scope == "user")
                {
                    UId = Context.User.Id;
                    GId = level is null ? Context.Guild.Id : null;
                }
                else
                {
                    GId = Context.Guild.Id;
                    Cid = level is not null ? ((IChannel)level).Id : null;
                }
                SettingLookupInfo info = new(setting) { CId = Cid, GId = GId, UId = UId };
                switch (action)
                {
                    case "set":
                        model.SetSetting(info, value);
                        await ReplyAsync($"The value for setting `{setting}` was changed to `{objValue}`");
                        break;
                    case "reset":
                        model.SetSetting(info, null);
                        await ReplyAsync($"Value for setting `{setting}` has been set back to its default");
                        break;
                    case "get":
                        model.GetSetting(info, out objValue, true);
                        await ReplyAsync($"The value for setting `{setting}` is currently `{objValue}`");
                        break;
                }
            }
            [Command("")]
            public async Task Configure()
            {
                var eb = new EmbedBuilder()
                {
                    Title = "Usage",
                    Description =
                    "`configure` `user` `<setting>` `set` `<value>` `[global]`\n" +
                    "or\n" +
                    "`configure` `user` `<setting>` `reset|get` `[global]`\n" +
                    "or" +
                    "`configure` `server` `<setting>` `set` `<value>` `[<channel>]`\n" +
                    "or\n" +
                    "`configure` `server` `<setting>` `reset|get` `[<channel>]`"
                };
                await ReplyAsync(embed: eb.Build());
            }

            private async Task<bool> VerifyPermissions(SettingModel settingModel)
            {
                var user = Context.User as IGuildUser;
                if (user.GuildPermissions.Has(GuildPermission.Administrator))
                    return true;
                SettingLookupInfo info = new("admin:bot-role-name") { GId = Context.Guild.Id };
                model.GetSetting(info, out string roleName, true);
                if (settingModel.PermissionRole && GetUserRoleNames(user).Contains(roleName))
                    return true;
                foreach (var permission in settingModel.GuildPermissions)
                    if (user.GuildPermissions.Has(permission))
                        return true;
                foreach (var permission in settingModel.ChannelPermissions)
                    if (user.GetPermissions(Context.Channel as IGuildChannel).Has(permission))
                        return true;

                string message = "";
                if (settingModel.PermissionRole)
                {
                    message += $"A role named `{roleName}`\n\n";
                }
                if (settingModel.GuildPermissions.Count > 0)
                {
                    message += $"Server permissions```";
                    foreach (var permission in settingModel.GuildPermissions)
                    {
                        message += "\n" + permission.ToString();
                    }
                    message += "```\n\n";
                }
                if (settingModel.ChannelPermissions.Count > 0)
                {
                    message += "Channel permissions```";
                    foreach (var permission in settingModel.ChannelPermissions)
                    {
                        message += "\n" + permission.ToString();
                    }
                    message += "```";
                }
                var eb = new EmbedBuilder()
                {
                    Title = "Insufficient permissions, must have either of the following to alter this setting:",
                    Description = message
                };

                await ReplyAsync(embed: eb.Build());

                return false;
            }
            private List<string> GetUserRoleNames(IGuildUser user)
            {
                var result = new List<string>(user.RoleIds.Count);
                foreach (var roleId in user.RoleIds)
                {
                    result.Add(Context.Guild.GetRole(roleId).Name);
                }
                return result;
            }
        }
    }
}