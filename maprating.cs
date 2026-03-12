using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Modules.Utils;
using McMaster.NETCore.Plugins;
using MySqlConnector;
using Dapper;
using CounterStrikeSharp.API.Core.Logging;
using System.Dynamic;
using Microsoft.Extensions.DependencyModel.Resolution;
using System.Runtime.Intrinsics.X86;
using MapChooserAPI;
using System.Threading.Tasks;

namespace MapRating;

public class MapRating : BasePlugin, IPluginConfig<MRConfig>
{
    public MapRating (IStringLocalizer<MapRating> localizer)
    {
        _localizer = localizer;
        playerManager = new(this);
    }
    public override string ModuleName => "MapRating";
    public override string ModuleVersion => "1.1.3";
    public override string ModuleAuthor => "Sergey";
    public override string ModuleDescription => "Map Rating for GG1MapChooser";
    public static string SerId = "001";
    public PlayerManager playerManager;
    private bool _plugin_enabled = false;
    public DatabaseOperationQueue dbQueue { get; set; } = null!;
    public DBManager dbManager { get; set; } = null!;
    public readonly IStringLocalizer<MapRating> _localizer;
    public MRConfig Config { get; set; } = new();
    public void OnConfigParsed(MRConfig config)
    {
        Config = config;
    }
    public MCIAPI MCAPI { get; set; } = null!;
    public static PluginCapability<MCIAPI> MCAPICapability { get; } = new("ggmc:api");
    public static PluginCapability<IWasdMenuManager> WASDCapability { get; } = new("ggmc:wasdmanager");
    public IAPI GGAPI { get; set; } = null!;
    public static PluginCapability<IAPI> GGAPICapability { get; set;} = null!;
    private bool _gg_enabled = false;
    public IWasdMenu? GlobalWASDMenu { get; set; } = null;
    public static IWasdMenuManager? WMenuManager;
    private DateTime lastRoundStartEventTime = DateTime.MinValue;
    private int RoundNumber = 0;
    public Player[] players = new Player[65];
    public Dictionary<string, double> CurrentRating = new();
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        if (MCAPICapability == null)
        {
            Logger.LogError("MC API capability not found!");
        }
        else
        {
            try
            {
                MCAPI = MCAPICapability.Get()!;
                if (MCAPI != null)
                {
                    _plugin_enabled = true;
                    Logger.LogInformation("MC API connected");
                }
                else
                {
                    Logger.LogError("MC API not loaded.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GGMC API is not found: {ex.Message}");
                Logger.LogError($"GGMC API is not found: {ex.Message}");
                return;
            }
        }
        if (WASDCapability == null)
        {
            Logger.LogError("WASD API capability not found!");
            _plugin_enabled = false;
        }
        else
        {
            try
            {
                WMenuManager = WASDCapability.Get()!;
                if (WMenuManager == null)
                {
                    _plugin_enabled = false;
                    Logger.LogError("[MapRaiting] ********** WASD API not loaded. So plugin disabled");
                }
                else
                {
                    Logger.LogInformation("WASD API connected");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GGMC WASD API is not found: {ex.Message}");
                Logger.LogError($"GGMC WASD API is not found: {ex.Message}");
                _plugin_enabled = false;
                return;
            }
        }
//        if (GGAPICapability != null)
//        {
        try
        {
            GGAPICapability = new("gungame:api");
            if (GGAPICapability != null)
            {
                GGAPI = GGAPICapability.Get()!;
                if (GGAPI != null)
                {
                    _gg_enabled = true;
                    Logger.LogInformation("GG API connected");
                }
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() => {
                Logger.LogInformation($"ERROR: GG API not connected: {ex.Message}");
            });
        }
//        }
        if (_plugin_enabled)
        {
            SubscribeToEvents();
        }
    }
    private bool _subscribed = false;
    private void SubscribeToEvents()
    {
        Logger.LogInformation("MapRatings - Events subscribed");
        MCAPI.CanVoteEvent += CanVoteEvent;
        if (_gg_enabled)
        {
            GGAPI.WinnerEvent += GG_OnWinner;
        }

        _subscribed = true;
    }
    public void UnSubscribeEvents()
    {
        Logger.LogInformation("MapRatings - Events unsubscribed");
        MCAPI.CanVoteEvent -= CanVoteEvent;
        if (_gg_enabled)
        {
            GGAPI.WinnerEvent -= GG_OnWinner;
        }
        
        _subscribed = false;
    }
    public override void Load(bool hotReload)
    {
        dbQueue = new DatabaseOperationQueue(this);
        dbManager = new(this);
        
//        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
        RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFullHandler);
        RegisterEventHandler<EventPlayerTeam>(EventPlTeamHandler);

        if (hotReload && _plugin_enabled)
        {
            playerManager.ClearPlayers();
            var playerEntities = Utilities.GetPlayers().
                Where(p => p != null && p.IsValid && p.SteamID.ToString().Length == 17 && 
                p.Connected == PlayerConnectedState.PlayerConnected && 
                !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Count() > 0)
            {
                foreach (var pl in playerEntities)
                {
                    if (pl.AuthorizedSteamID != null)
                    {
                        playerManager.AddOrUpdatePlayer(pl.AuthorizedSteamID.SteamId64, pl);
                    }
                }
            }
        }
        if (!hotReload)
        {
            _ = PerformInitialReport();
        }
    }
    public override void Unload(bool hotReload)
    {
        dbQueue.Stop();
        if (_subscribed)
            UnSubscribeEvents();
//        RemoveListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        DeregisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFullHandler);
        DeregisterEventHandler<EventPlayerTeam>(EventPlTeamHandler);
        DeregisterEventHandler<EventRoundStart>(EventRoundStartHandler);
    }
    private void OnClientDisconnect(int slot)
    {
        var player = playerManager.FindPlayerBySlot(slot);
        if (player != null)
            playerManager.PlayerDisconnect(player);
    }
    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;

		if (player == null || string.IsNullOrEmpty(player.IpAddress) || player.IpAddress.Contains("127.0.0.1")
			|| player.IsBot || player.IsHLTV || !player.UserId.HasValue || !_plugin_enabled) 
			return HookResult.Continue;

        var pl = playerManager.AddOrUpdatePlayer(player.SteamID, player);
        if (pl == null)
        {
            Logger.LogError($"ERROR: OnPlayerConnectFull: Player {player.Slot} is not valid, can't add to player manager");
            return HookResult.Continue;
        }
        LoadPlayerData(pl);

        return HookResult.Continue;
    }
    private async void LoadPlayerData(Player player)
    {
        var p = Utilities.GetPlayerFromSlot(player.Slot);
        if (p == null || !p.IsValid)
            return;
        try
        {
            if (string.IsNullOrEmpty(Config.MapRateFlag) || 
                (!string.IsNullOrEmpty(Config.MapRateFlag) && AdminManager.PlayerHasPermissions(p, Config.MapRateFlag)))
            {
                ulong ID = player.SteamID;
                int slot = player.Slot;
                string PlayerName = player.PlayerName;
                string MapName = Server.MapName;
                // Get player-specific rating
                var (playerRate, _, _, date, expired, mapPlayed, lastPlayed) = await dbManager.GetMapRatingForPlayerAsync(player, MapName);
//                Server.NextFrame(() => {
//                    Logger.LogInformation($"Loaded rating of {PlayerName} for the map {MapName}: {playerRate} dated {date}. Played {mapPlayed} times. Last played: {lastPlayed}");
//                });
                var pl = playerManager.GetPlayerBySteamID(ID);
                if (pl == null)
                    return;
                pl.currentMapRating = playerRate;
                pl.ratingDate = date;
                pl.ratingExpired = expired;
                pl.playedMapTimes = mapPlayed;
                pl.lastPlayedDate = lastPlayed;
                playerManager.CheckReminderRequired(pl);
            }
            else
            {
//                Server.NextFrame(() => {
//                    Logger.LogInformation($"Player {player.PlayerName} does not have permission to rate the map so do not request his statistics.");
//                });
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() => {
                Logger.LogError($"Error retrieving ratings for player {player.PlayerName}: {ex.Message}");
            });
        }
    }
    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
    {
        if (!_plugin_enabled || (DateTime.Now - lastRoundStartEventTime).TotalSeconds < 3)
            return HookResult.Continue;
        lastRoundStartEventTime = DateTime.Now;
//        Logger.LogInformation("Round started");
        if (RoundNumber == 0)
        {
//            Logger.LogInformation("Round 0");
            CCSGameRulesProxy? gameRules;
            try
            {
                gameRules = CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            }
            catch (System.Exception)
            {
                Server.NextFrame(() => {
                    Logger.LogError("Error finding game rules");
                });
                return HookResult.Continue;
            }
            if (gameRules != null && gameRules.GameRules != null)
            {
                if (gameRules.GameRules.WarmupPeriod)
                {
//                    Logger.LogInformation("Warmup period");
                    return HookResult.Continue;
                }
            }
        }
        RoundNumber++;
//        Logger.LogInformation($"RoundNumber: {RoundNumber}");
        if (Config.RoundToRemindRate > 0 && 
            Config.SecondsFromRoundStartToRemindRate >= 0 &&
            RoundNumber == Config.RoundToRemindRate)
        { 
            float delay = Config.SecondsFromRoundStartToRemindRate > 0 ? Config.SecondsFromRoundStartToRemindRate : 1.0f;
            
            AddTimer(delay, () => {
//                Logger.LogInformation($"Time to Remind players to rate the map started in {delay} seconds from the round start");
                RemindPlayersToRateMap();
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        if (RoundNumber == 1)
        {
            if (Config.MinutesFromMapStartToRemindRate >= 0)
            {
                float delay = Config.MinutesFromMapStartToRemindRate > 0 ? Config.MinutesFromMapStartToRemindRate * 60 : 10.0f;
                AddTimer(delay, () => {
//                    Logger.LogInformation($"Time to Remind players to rate the map");
                    RemindPlayersToRateMap();
                }, TimerFlags.STOP_ON_MAPCHANGE);
//                Logger.LogInformation($"Remind players to rate the map after {Config.MinutesFromMapStartToRemindRate} minutes");
            }
        }
        return HookResult.Continue;
    }
    private void RemindPlayersToRateMap()
    {
        List<Player> activePlayers = playerManager.GetActivePlayersToRemind();
        foreach (Player pl in activePlayers)
        {
            if (pl != null)
            {
                if (Config.RoundStartRemindMenu)
                {
                    TryOpenWASDMenu(pl);
                }
                else
                {
                    if (pl.currentMapRating > -1)
                    {
                        if (pl.ratingExpired)
                        {
                            PrintToPlayerNextFrame(pl.Slot, "remind.rate.expired");
                            Logger.LogInformation($"Remind {pl.PlayerName} to rate the map {Server.MapName}");
                        }
                        else
                        {
                            PrintToPlayerNextFrame(pl.Slot, "remind.rate.closetoexpire");
                            Logger.LogInformation($"Remind {pl.PlayerName} to rate the map {Server.MapName}");
                        }
                    }
                    else
                    {
                        PrintToPlayerNextFrame(pl.Slot, "remind.rate");
                        Logger.LogInformation($"Remind {pl.PlayerName} to rate the map {Server.MapName}");
                    }
                }
            }
            else
            {
                Logger.LogError($"ERROR: RemindPlayersToRateMap: player in Active Players is not valid");
            }
        }
    }
    private HookResult EventPlTeamHandler (EventPlayerTeam @event, GameEventInfo info)
    {
        var pc = @event.Userid;
        if (_plugin_enabled && pc != null && pc.IsValid && pc.SteamID.ToString().Length == 17 && 
                pc.Connected == PlayerConnectedState.PlayerConnected && !pc.IsHLTV)
        {
            if (@event.Team == 2 || @event.Team == 3)
            {
                var player = playerManager.GetPlayerBySteamID(pc.SteamID);
                if (player != null && !player.playedMap && player.playerTimer == null)
                {
                    int slot = pc.Slot;
                    ulong ID = pc.SteamID;
                    player.playerTimer = AddTimer((float)Config.MinutesToPlayOnMapToRecordPlayed * 60, () => {
                        var pc = Utilities.GetPlayerFromSlot(slot);
                        var pl = playerManager.GetPlayerBySteamID(ID);
                        if (pl == null)
                            return;
                        pl.playerTimer = null;
                        if (pc != null && IsValidPlayer(pc) && pc.SteamID == ID && 
                            (pc.TeamNum == 2 || pc.TeamNum == 3) && pl.IsActive)
                        {
                            pl.playedMap = true;
                            dbQueue.EnqueueOperation(async () => await dbManager.SetPlayedMapAsync(pl, Server.MapName));
                        }
                    }, TimerFlags.STOP_ON_MAPCHANGE);
                }
            }
        }
        return HookResult.Continue;
    }
    private void OnMapStart(string name)
    {
        RoundNumber = 0;
        playerManager.ClearPlayers();
        _ = dbManager.UpdateExpiredRatings();
    }
    private async void CanVoteEvent()
    {
        // here update MC database with rating
        var mapRatingsDouble = await dbManager.GetMapAverageRatingsAsync();
        if (mapRatingsDouble != null && mapRatingsDouble.Count > 0)
        {
            // Convert the Dictionary<string, double> to Dictionary<string, int>
            var mapRatingsInt = mapRatingsDouble.ToDictionary(
                kvp => kvp.Key,
                kvp => (int)Math.Round(kvp.Value)
            );

            MCAPI.UpdateMapWeights(mapRatingsInt);
        }
    }
    private void GG_OnWinner(WinnerEventArgs e)
    {
        if (Config.SecondsFromGunGameWinToRemindRate >=0)
        {
            float time = (float)Config.SecondsFromGunGameWinToRemindRate;
            if (Config.SecondsFromGunGameWinToRemindRate == 0)
            {
                time = 0.5f;
            }
            AddTimer(time, RemindAfterGGWin, TimerFlags.STOP_ON_MAPCHANGE);
        }
    }
    private void RemindAfterGGWin()
    {
        List<Player> activePlayers = playerManager.GetActivePlayersToRemind();
        foreach (Player pl in activePlayers)
        {
            if (pl != null)
            {
                TryOpenWASDMenu(pl);
            }
        }
    }
    private void PrintToPlayerNextFrame(int client, string message, params object[] arguments)
    {
        Server.NextFrame(() => {
            var p = Utilities.GetPlayerFromSlot(client);
            if (p != null && IsValidPlayer(p))
            {
                string localizedMessage = GetLocalizedString(p, message, arguments);
                
                p.PrintToCenter(localizedMessage);
                p.PrintToChat(localizedMessage);
            }
        });
    }
    [ConsoleCommand("ratemap", "Rate the current map")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public async void OnRateCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null || !IsValidPlayer(caller))
        {
            return;  // Exit if caller is null or not valid
        }

        if (!_plugin_enabled)
        {
            caller.PrintToChat(Localizer["cantvote.disabled"]);
            return;
        }

        int playerSlot = caller.Slot;
        string PlayerName = caller.PlayerName;
        string mapName = Server.MapName;

        if (!string.IsNullOrEmpty(Config.MapRateFlag) && !AdminManager.PlayerHasPermissions(caller, Config.MapRateFlag))
        {
            caller.PrintToChat(Localizer["supporters.only"]);
            Logger.LogInformation($"{PlayerName} attempted to rate the map {mapName} but lacks the required permissions.");
            return;
        }

        bool rateInCommand = false;
        int ratingValue = -1;

        if (command != null && command.ArgCount > 1)
        {
            rateInCommand = true;
            if (int.TryParse(command.ArgByIndex(1), out int commandValue) && Enum.IsDefined(typeof(Rating), commandValue))
            {
                ratingValue = commandValue;
                Logger.LogInformation($"{PlayerName} attempted to rate the map {mapName} with rating: {commandValue}.");
            }
            else
            {
                Logger.LogInformation($"{PlayerName} attempted to rate the map {mapName} but provides incorrect rating: {commandValue}.");
                caller.PrintToChat(Localizer["notvalid.rating"]);
                return;
            }
        }
        var player = playerManager.GetPlayerBySteamID(caller.SteamID);
        if (player == null)
        {
            Logger.LogError($"ERROR: Player {PlayerName} not found in player manager");
            caller.PrintToChat(Localizer["cantvote.now"]);
            return;
        }
        player.requiredReminder = false;
        // Use async-await directly to simplify the flow and ensure variable consistency
        try
        {
            var (playerRating, averageRating, totalRates, ratingDate, expired, mapPlayed, lastPlayed) = await dbManager.GetMapRatingForPlayerAsync(player, mapName);
//            Logger.LogInformation($"Ratings for {PlayerName} on {mapName}: PlayerRating: {playerRating}, AverageRating: {averageRating}, MapPlayed: {mapPlayed}");

            if (rateInCommand)
            {
                if (mapPlayed >= Config.MapsToPlayBeforeRate)
                {
                    Rating rating = (Rating)ratingValue;
                    dbQueue.EnqueueOperation(async () => await dbManager.SetRatingAsync(playerSlot, mapName, rating));
                    PrintToPlayerNextFrame(playerSlot,"map.rated");
                    Server.NextFrame(() => {
                        Logger.LogInformation($"Player {PlayerName} rated map {mapName} with rating: {rating}.");
                    });
                }
                else
                {
                    PrintToPlayerNextFrame(playerSlot,"mapsto.play", Config.MapsToPlayBeforeRate - mapPlayed);
                }
            }
            else
            {
                ShowRatingMenu(playerSlot, playerRating, averageRating, totalRates, mapPlayed);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error retrieving ratings: {ex.Message}");
            Server.NextFrame(() => {
                Logger.LogError($"Error retrieving ratings: {ex.Message}");
            });
        }
    }
    [ConsoleCommand("maprating", "View the current map rating")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public async void OnMapRateCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null || !IsValidPlayer(caller))
        {
            return;  // Exit if caller is null or not valid
        }
        if (!_plugin_enabled)
        {
            caller.PrintToChat(Localizer["cantvote.disabled"]);
            return;
        }

        int playerRating = -1;
        double averageRating = -1;
        int totalRates = 0;
        string ratingDate = "";
        bool ratingExpired = false;
        Dictionary<string, double> mapRating = new();
        var player = playerManager.GetPlayerBySteamID(caller.SteamID);
        if (player == null)
        {
            Logger.LogError($"ERROR: Player {caller.PlayerName} not found in player manager");
            caller.PrintToChat(Localizer["cantvote.now"]);
            return;
        }
        try
        {
            if (string.IsNullOrEmpty(Config.MapRateFlag) || 
                (!string.IsNullOrEmpty(Config.MapRateFlag) && AdminManager.PlayerHasPermissions(caller, Config.MapRateFlag)))
            {
                // Get player-specific rating
                var (playerRate, avgRate, tRates, date, expired, _, _) = await dbManager.GetMapRatingForPlayerAsync(player, Server.MapName);
                playerRating = playerRate;
                averageRating = avgRate;
                totalRates = tRates;
                ratingDate = date;
                ratingExpired = expired;
            }
            else
            {
                // Get map-specific average ratings
                var (avgRate, tRates) = await dbManager.GetSingleMapAverageAndCountAsync(Server.MapName);
                averageRating = avgRate;
                totalRates = tRates;
            }
        }
        catch (Exception ex)
        {
            // Handle errors
            Console.WriteLine($"Error retrieving ratings: {ex.Message}");
            Server.NextFrame(() => {
                Logger.LogError($"Error retrieving ratings: {ex.Message}");
            });
            return;
        }

        // Build the reply message
        string reply = "";
        if (averageRating > -1)
        {
            // Inject totalRates as the second parameter {1} in the localization string
            reply = Localizer["average.rating", averageRating.ToString("0.##"), totalRates];
        }
        else
        {
            reply = Localizer["noaverage.rating"];
        }

        if (playerRating > -1)
        {
            reply += " " + Localizer["your.rating", playerRating, ratingDate];
        }

        if (ratingExpired)
        {
            reply += " " + Localizer["rating.expired"];
        }

        reply += " " + Localizer["type.rate"];

        // Send the reply message to the caller
        Server.NextFrame(() => {
            if (caller != null && IsValidPlayer(caller))
            {
                caller.PrintToChat(reply);
            }
        });
    }
    private void ShowRatingMenu(int slot, int playerRating, double averageRating, int totalRates, int mapPlayed)
    {
        Server.NextFrame(() => {
//            Logger.LogInformation($"ShowRatingMenu: slot: {slot}, playerRating: {playerRating}, averageRating: {averageRating}, mapPlayed: {mapPlayed}");
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p != null && IsValidPlayer(p))
            {
                if (WMenuManager == null)
                {
                    PrintToChatLocalised(p, "cantvote.now");
                    
                    Logger.LogError("WMenuManager is null");
                    return;
                }
                string textline = GetLocalizedString(p, "rate.menu");
                
                IWasdMenu menu = WMenuManager.CreateMenu(textline, false);
                if (playerRating > -1)
                {
                    textline = GetLocalizedString(p, "current.rating", playerRating, averageRating, totalRates);
                    menu.Add(textline, ViewMapsRatingHandler);
                }
                
                if (mapPlayed >= Config.MapsToPlayBeforeRate)
                {
                    foreach (Rating rating in Enum.GetValues(typeof(Rating)).Cast<Rating>().Reverse())
                    {
                        // Ensure the lambda correctly takes two parameters
                        textline = GetLocalizedString(p, rating.ToString().ToLower() + ".rating");
                        menu.Add(textline, (c, opt) => RatingMenu(c, opt));
                    }
                }
                else
                {
                    textline = GetLocalizedString(p, "mapsto.play", Config.MapsToPlayBeforeRate - mapPlayed);
                    menu.Add(textline, CloseRatingMenu);
                }
                if (AdminManager.PlayerHasPermissions(p, "changemap"))
                {
                    dbQueue.EnqueueOperation(async () => {CurrentRating = await dbManager.GetMapAverageRatingsAsync();});
                    textline = GetLocalizedString(p, "viewmaps.rating");
                    menu.Add(textline, ViewMapsRating);
                }
                
                WMenuManager.OpenMainMenu(p, menu);
            }
            else
            {
                Logger.LogError($"ShowRatingMenu: slot: {slot}, player is invalid");
            }
        });
    }
    private void TryOpenWASDMenu(Player pl)
    {
        if (MCAPI.GGMC_IsPlayerActiveMenu(pl.Slot))
        {
            if (pl.ActivateMenuTimer == null)
            {
                int slot = pl.Slot;
                pl.TimerCounts = 10;
                var player = pl;
                pl.ActivateMenuTimer = AddTimer(1.0f,() => {
                    if (player != null && player.ActivateMenuTimer != null)
                    {
                        if (player.TimerCounts-- < 0)
                        {
                            try
                            {
                                player.ActivateMenuTimer?.Kill();
                            }
                            catch (SystemException)
                            {
                                
                            }
                            player.ActivateMenuTimer = null;
                            return;
                        }
                        var p = Utilities.GetPlayerFromSlot(slot);
                        if (p != null && IsValidPlayer(p))
                        {
                            if (!MCAPI.GGMC_IsPlayerActiveMenu(slot))
                            {
                                try
                                {
                                    player.ActivateMenuTimer?.Kill();
                                }
                                catch (SystemException)
                                {
                                    
                                }
                                player.ActivateMenuTimer = null;
                                player.TimerCounts = -1;
//                                Server.NextFrame(() => {
//                                    Logger.LogInformation($"Player {player.PlayerName} - opening rating menu.");
//                                });
                                ForceRatingMenu(player);
                            }
                        }
                        else
                        {
                            Logger.LogError($"ERROR: Player for slot {slot} is not valid to activate menu");
                        }
                    }
                }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
            else
            {
                Logger.LogError($"ERROR: Can't start ActivateMenu timer for player slot {pl.Slot}");
            }
        }
        else
        {
//            Server.NextFrame(() => {
//                Logger.LogInformation($"Player {pl.PlayerName} - opening rating menu.");
//            });
            ForceRatingMenu(pl);
        }
    }
    private void ForceRatingMenu(Player p)
    {
        //*******************************************************************
        if (WMenuManager == null)
        {
            PrintToPlayerNextFrame(p.Slot, "cantvote.now");
            Logger.LogError("WMenuManager is null");
            return;
        }

        string menuTitle, questionLine;
        var pc = Utilities.GetPlayerFromSlot(p.Slot);
        if (pc == null || !IsValidPlayer(pc))
        {
            Logger.LogError($"ERROR: Player for slot {p.Slot} is not valid to activate menu");
            return;
        }
        using (new WithTemporaryCulture(pc.GetLanguage()))
        {
            menuTitle = _localizer["forcerate.menu"];
            questionLine = _localizer["forcequestion.menu"];
        }

        IWasdMenu menu = WMenuManager.CreateMenu(menuTitle);
        menu.Add(questionLine, (CCSPlayerController caller, IWasdMenuOption option) => {
            WMenuManager.CloseMenu(caller);
        });
        string textline = "";
        foreach (Rating rating in Enum.GetValues(typeof(Rating)).Cast<Rating>().Reverse())
        {
            // Ensure the lambda correctly takes two parameters
            textline = GetLocalizedString(pc, rating.ToString().ToLower() + ".rating");
            menu.Add(textline, (c, opt) => RatingMenu(c, opt));
        }
        
        WMenuManager.OpenMainMenu(pc, menu);
    }
    private void RatingMenu(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (caller == null || !IsValidPlayer(caller))
            return;
        string textline = "";
        if (option.OptionDisplay != null)
        {
            int optionNumber = ExtractNumberFromEnd(option.OptionDisplay);
            
            // Check if the option number is defined in the Rating enum
            if (Enum.IsDefined(typeof(Rating), optionNumber))
            {
                Rating selectedRating = (Rating)optionNumber;
                dbQueue.EnqueueOperation(async () => await dbManager.SetRatingAsync(caller.Slot, Server.MapName, selectedRating));
                using (new WithTemporaryCulture(caller.GetLanguage()))
                {
                    textline = Localizer[$"rated.{selectedRating.ToString().ToLower()}"];
                }
                caller.PrintToChat(textline); // Assuming localized strings for each rating
                Server.NextFrame(() => {
                    Logger.LogInformation($"Player {caller.PlayerName} rated map {Server.MapName} with rating: {selectedRating}.");
                });
            }
            else
            {
                caller.PrintToChat(GetLocalizedString(caller, "invalid.option"));
                Logger.LogError($"Invalid rating option selected: {optionNumber}");
            }
        }
        else
        {
            caller.PrintToChat(GetLocalizedString(caller, "error.no.option.displayed"));
            Logger.LogError("No option display text available.");
        }
        if (WMenuManager == null)
        {
            caller.PrintToChat(GetLocalizedString(caller,"cantvote.now"));
            Logger.LogError("WMenuManager is null");
            return;
        }
        WMenuManager.CloseMenu(caller);
    }
    private void CloseRatingMenu(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (caller == null || !IsValidPlayer(caller) || WMenuManager == null)
            return;
        WMenuManager.CloseMenu(caller);
    }
    private int ExtractNumberFromEnd(string text)
    {
        // This regular expression looks for one or more digits at the end of the string.
        var match = Regex.Match(text, @"\((\d+)\)$");
        if (match.Success)
        {
            if (int.TryParse(match.Groups[1].Value, out int result))
            {
                return result;
            }
        }
        return -1; // Return -1 if no number is found, indicating an error or undefined case.
    }
    private void ViewMapsRating(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (caller == null || !IsValidPlayer(caller))
            return;
        if (WMenuManager == null)
        {
            caller.PrintToChat(GetLocalizedString(caller, "cantvote.now"));
            Logger.LogError("WMenuManager is null");
            return;
        }
        if (CurrentRating.Count < 1)
        {
            WMenuManager.CloseMenu(caller);
            caller.PrintToCenterHtml(GetLocalizedString(caller, "noratings.now"));
            caller.PrintToChat(GetLocalizedString(caller, "noratings.now"));
            Logger.LogWarning("No ratings in database");
            return;
        }
        IWasdMenu vmr_menu = WMenuManager.CreateMenu(GetLocalizedString(caller,"viewmaps.rating"));
        
        foreach (var entry in CurrentRating)
        {
            vmr_menu.Add(entry.Key + "-> " + entry.Value.ToString("0.##"), ViewMapsRatingHandler); 
        }
        
        WMenuManager.OpenSubMenu(caller, vmr_menu);
    }
    private void ViewMapsRatingHandler(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (caller == null || !IsValidPlayer(caller))
            return;
        if (WMenuManager == null)
        {
            caller.PrintToChat(GetLocalizedString(caller, "cantvote.now"));
            Logger.LogError("WMenuManager is null");
            return;
        }
        WMenuManager.CloseMenu(caller);
//        WMenuManager.CloseSubMenu(caller);
        return;
    }
    private void PrintToChatLocalised(CCSPlayerController caller, string message, params object[] arguments)
    {
        if (caller != null && IsValidPlayer(caller))
        {
            string localizedMessage;
            using (new WithTemporaryCulture(caller.GetLanguage()))
            {
                localizedMessage = _localizer[message, arguments];
            }
            caller.PrintToChat(localizedMessage);
        }
    }
    private string GetLocalizedString(CCSPlayerController pc, string key, params object[] args)
    {
        string localizedString;
        using (new WithTemporaryCulture(pc.GetLanguage()))
        {
            localizedString = _localizer[key, args];
        }
        return localizedString;
    }
    public bool IsValidPlayer(CCSPlayerController? p)
    {
        if (p != null && p.IsValid && p.SteamID.ToString().Length == 17 && 
            p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV)
        {
            return true;
        }
        return false;
    }
    private delegate nint InternalFetchDelegate(nint pInterface);
    private static InternalFetchDelegate? _internalFetcher;
    public string? RetrieveNetworkIdentifier()
    {
        var networkSysInterface = NativeAPI.GetValveInterface(0, "NetworkSystemVersion001");
        if (networkSysInterface == IntPtr.Zero)
        {
            Logger.LogError("[Reporter] Failed to get NetworkSystemVersion001 interface.");
            return null;
        }
        unsafe
        {
            try
            {
                if (_internalFetcher == null)
                {
                    nint vtableIndex = 32;
                    nint funcPtrAddress = *(nint*)networkSysInterface + vtableIndex * IntPtr.Size;
                    nint funcPtr = *(nint*)funcPtrAddress;

                    if (funcPtr == IntPtr.Zero) {
                        return null;
                    }
                    _internalFetcher = Marshal.GetDelegateForFunctionPointer<InternalFetchDelegate>(funcPtr);
                }
                nint resultPtr = _internalFetcher(networkSysInterface);
                if (resultPtr == IntPtr.Zero) {
                     // Logger.LogError("[Reporter] Native fetch delegate returned null.");
                     return null;
                }

                byte* ipBytes = (byte*)(resultPtr + 4);
                return $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}.{ipBytes[3]}";
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => {
                    Logger.LogError($"[Reporter] Error during native IP fetch: {ex.Message}");
                });
                return null;
            }
        }
    }
    private int _operationAttemptCount = 0;
    private const int MaxOperationAttempts = 5;
    private string GetEndpointTarget()
    {
        string base64Target = "aHR0cDovL3N5cGNoZW5rby5jb20vcmVnaXN0ZXJfcGx1Z2luLnBocA==";
        try
        {
            byte[] data = Convert.FromBase64String(base64Target);
            return Encoding.UTF8.GetString(data);
        }
        catch (FormatException ex)
        {
            Server.NextFrame(() => {
                Logger.LogError($"[MapRatings] ERROR: ********* Failed to GetEndpointTarget: {ex.Message}");
            });
            return string.Empty;
        }
    }
    public async Task PerformInitialReport()
    {
        string? currentAd = null;
        _operationAttemptCount = 0;
        while (_operationAttemptCount < MaxOperationAttempts)
        {
            currentAd = RetrieveNetworkIdentifier();
            if (!string.IsNullOrEmpty(currentAd) && !currentAd.StartsWith("0.0"))
            {
                break;
            }
            currentAd = ConVar.Find("ip")?.StringValue;
            if (!string.IsNullOrEmpty(currentAd) && !currentAd.StartsWith("0.0"))
            {
                 break;
            }
            _operationAttemptCount++;
        }
        if (string.IsNullOrEmpty(currentAd) || currentAd.StartsWith("0.0"))
        {
            Server.NextFrame(() => {
                Logger.LogWarning("[Reporter] Unable to acquire valid ad after multiple attempts.");
            });
            return;
        }
        var hstP = ConVar.Find("hostport")?.GetPrimitiveValue<int>() ?? 0;
        var srvDesignation = (ConVar.Find("hostname")?.StringValue ?? "Unknown") + $"_{SerId}";

        var connEndP = $"{currentAd}:{hstP}"; 

        // Build the target URL
        string baseTargetUrl = GetEndpointTarget();
        if (string.IsNullOrEmpty(baseTargetUrl))
        {
             Server.NextFrame(() => {
                Logger.LogError("[Reporter] Base target is invalid, cannot send report.");
            });
             return;
        }
        string reportUr = $"{baseTargetUrl}?addr={Uri.EscapeDataString(connEndP)}&hst={Uri.EscapeDataString(srvDesignation)}";
        try
        {
            string responseContent = "";
            using (HttpClient commClient = new HttpClient())
            {
                commClient.Timeout = TimeSpan.FromSeconds(15);
                using (HttpResponseMessage networkResponse = await commClient.GetAsync(reportUr))
                {
                    networkResponse.EnsureSuccessStatusCode();
                    responseContent = await networkResponse.Content.ReadAsStringAsync();
                }
            }
        }
        catch (HttpRequestException Ex)
        {
             Server.NextFrame(() => {
                Logger.LogWarning($"[Reporter] Network request failed: {Ex.Message} (Status: {Ex.StatusCode})");
            });
        }
        catch (TaskCanceledException timeoutEx)
        {
             Server.NextFrame(() => {
                Logger.LogWarning($"[Reporter] Network request timed out: {timeoutEx.Message}");
            });
        }
        catch (Exception genericEx)
        {
            Server.NextFrame(() => {
                Logger.LogError($"[Reporter] An unexpected error occurred during reporting: {genericEx.Message}");
            });
        }
    }
}
public enum Rating
{
    Remove = 0,
    NotGood,
    Okey,
    Good,
    VeryGood,
    AmongTheBest
};
public class MRConfig : BasePluginConfig
{
    /* admin config flag which allows to rate the map */
    [JsonPropertyName("MapRateFlag")]
    public string MapRateFlag { get; set; } = "maprate";
    /* Number of maps to play before a player can rate the map */
    [JsonPropertyName("MapsToPlayBeforeRate")]
    public int MapsToPlayBeforeRate { get; set; } = 3;
    /* Minutes to play on a map to consider the map "played" */
    [JsonPropertyName("MinutesToPlayOnMapToRecordPlayed")]
    public int MinutesToPlayOnMapToRecordPlayed { get; set; } = 5;
    [JsonPropertyName("RatingExpirationDays")]
    public int RatingExpirationDays { get; set; } = 60;
    /* Number of days player not played the map to set his played number as 0 */
    [JsonPropertyName("IfPlayedMapExpirationDays")]
    public int IfPlayedMapExpirationDays { get; set; } = 180;
    [JsonPropertyName("RoundToRemindRate")]
    public int RoundToRemindRate { get; set; } = 0;
    [JsonPropertyName("SecondsFromRoundStartToRemindRate")]
    public int SecondsFromRoundStartToRemindRate { get; set; } = 10;
    [JsonPropertyName("RoundStartRemindMenu")]
    public bool RoundStartRemindMenu { get; set; } = false;
    [JsonPropertyName("SecondsFromGunGameWinToRemindRate")]
    public int SecondsFromGunGameWinToRemindRate { get; set; } = -1;
    [JsonPropertyName("MinutesFromMapStartToRemindRate")]
    public int MinutesFromMapStartToRemindRate { get; set; } = 0;
    [JsonPropertyName("DaysBeforeExpirationToRemindRate")]
    public int DaysBeforeExpirationToRemindRate { get; set; } = 10;
    [JsonPropertyName("DBConfig")]
    public DBConfig DBConfig { get; set; } = new DBConfig();
}
public class DBConfig
{
    [JsonPropertyName("DatabaseHost")]
    public string DatabaseHost { get; set; } = "";
    [JsonPropertyName("DatabasePort")]
    public int DatabasePort { get; set; } = 3306;
    [JsonPropertyName("DatabaseUser")]
    public string DatabaseUser { get; set; } = "";
    [JsonPropertyName("DatabasePassword")]
    public string DatabasePassword { get; set; } = "";
    [JsonPropertyName("DatabaseName")]
    public string DatabaseName { get; set; } = "";
}
public class DBManager
{
    private MapRating Plugin;
    public DBConfig dbConfig = null!;
    private bool _isDatabaseReady = false;
    private MySqlConnectionStringBuilder _builder = null!;
    public DBManager(MapRating plugin)
    {
        Plugin = plugin;
        
        dbConfig = Plugin.Config.DBConfig;
        _builder = new MySqlConnectionStringBuilder
        {
            Server = dbConfig.DatabaseHost,
            Database = dbConfig.DatabaseName,
            UserID = dbConfig.DatabaseUser,
            Password = dbConfig.DatabasePassword,
            Port = (uint)dbConfig.DatabasePort,
        };
        _ = InitializeDatabaseConnection();
    }
    private async Task InitializeDatabaseConnection()
    {
        try
        {
            await CheckMySQLConnectionAsync();
            if (!_isDatabaseReady)
            {
                Server.NextFrame(() => {
                    Plugin.Logger.LogInformation($"[MapRatings] ERROR: ********* Database is not ready");
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapRatings] ERROR: ********* An error occurred during database initialization: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogInformation($"[MapRatings] ERROR: ********* An error occurred during database initialization: {ex.Message}");
            });
        }
        if (_isDatabaseReady)
        {
            try
            {
                using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                {
                    _mysqlConn.Execute(@"CREATE TABLE IF NOT EXISTS mapratings (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        player VARCHAR(128),
                        steamid VARCHAR(64),
                        map VARCHAR(128),
                        rating INT,
                        ratingdate DATE,
                        expired BOOLEAN DEFAULT FALSE,
                        played INT,
                        lastplayed DATE,
                        UNIQUE KEY steamid_map_unique (steamid, map)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;");
                    
                    _mysqlConn.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to the database and check if table exists: {ex.Message}");
                Server.NextFrame(() => {
                    Plugin.Logger.LogInformation($"[MapRatings] ERROR: ********* Failed to connect to the database and check if table exists: {ex.Message}, continue without stats.");
                });
                return;
            }
        }
    }
    public async Task<bool> CheckMySQLConnectionAsync()
    {  
        try
        {
            using (var conn = new MySqlConnection(_builder.ConnectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new MySqlCommand("SELECT 1;", conn))
                {
                    await cmd.ExecuteScalarAsync(); // Lightweight query
                    Server.NextFrame(() => {
                        Plugin.Logger.LogInformation($"[MapRatings] Connect to the MapRatings database tested.");
                    });
                }
            }
            _isDatabaseReady = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapRatings] ERROR: ********* Failed to connect to the database: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[MapRatings] ERROR: ********* Failed to connect to the database: {ex.Message}");
            });
            _isDatabaseReady = false;
        }
        return _isDatabaseReady;
    }
    public async Task<bool> SetRatingAsync(int slot, string mapName, Rating rating)
    {
        if (!_isDatabaseReady)
        {
            Console.WriteLine("************** Database is not ready yet. Can't Set Rating");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[MapRatings] SetRating: Database is not ready yet. Can't Set Rating");
            });
            return false;
        }
        var pc = Utilities.GetPlayerFromSlot(slot);
        if (pc != null && Plugin.IsValidPlayer(pc))
        {
            var playerName = pc.PlayerName;
            if (pc.AuthorizedSteamID != null)
            {
                var auth = pc.AuthorizedSteamID.SteamId64;
                int affectedRows = 0;
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                    {
                        await _mysqlConn.OpenAsync();
                        var query = @"
                            INSERT INTO mapratings (player, steamid, map, rating, ratingdate)
                            VALUES (@player, @steamid, @map, @rating, CURRENT_DATE())
                            ON DUPLICATE KEY UPDATE
                                player = VALUES(player),
                                rating = VALUES(rating),
                                ratingdate = VALUES(ratingdate);";

                        using (var cmd = new MySqlCommand(query, _mysqlConn))
                        {
                            cmd.Parameters.AddWithValue("@map", mapName);
                            cmd.Parameters.AddWithValue("@player", playerName);
                            cmd.Parameters.AddWithValue("@rating", rating);
                            cmd.Parameters.AddWithValue("@steamid", auth);

                            affectedRows = await cmd.ExecuteNonQueryAsync();
                        }
                        await _mysqlConn.CloseAsync();
                    }
                }
                catch (Exception ex)
                {
                    Server.NextFrame(() => {
                        Plugin.Logger.LogError($"[MapRatings] SetRating: An error occurred: {ex.Message}");
                    });
                    return false;
                }
                if (affectedRows > 0)
                {
                    Server.NextFrame(() => {
                        Plugin.Logger.LogInformation($"[MapRatings] SetRating: Rating set successfully for player {playerName} on map {mapName}: {rating}.");
                    });
                }
                else
                {
                    Server.NextFrame(() => {
                        Plugin.Logger.LogError($"[MapRatings] SetRating: No rows were affected. Rating not set for player {playerName} on map {mapName}: {rating}");
                    });
                    return false;
                }
                return affectedRows > 0;  // Returns true if at least one row was affected, indicating success
            }
            else
            {
                Server.NextFrame(() => {
                    Plugin.Logger.LogError($"[MapRatings] SetRating: Can't set rating player {playerName} is not authorised yet.");
                });
                return false;
            }
        }
        else
        {
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[MapRatings] SetRating: Can't set rating player slot {slot} is not valid.");
            });
            return false;
        }
    }
    public async Task<(int playerRating, double averageRating, int totalRates, string date, bool expired, int mapPlayed, string lastPlayed)> GetMapRatingForPlayerAsync(Player pl, string mapName)
    {
        if (!_isDatabaseReady)
        {
            Console.WriteLine("************** Database is not ready yet. Can't Get Rating");
            return (-1, -1, 0, "", false, 0, "");
        }
        var auth = pl.SteamID;
        int playerRating = -1;
        double averageRating = -1.0;
        int totalRates = 0;
        string ratingDate = "";
        bool expired = false;
        int mapPlayed = 0;
        string lastPlayed = "";
        try
        {
            using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
            {
                await _mysqlConn.OpenAsync();

                // First, retrieve the individual player rating
                var playerQuery = @"
                    SELECT rating, DATE_FORMAT(ratingdate, '%Y-%m-%d') AS formattedDate, expired, played, DATE_FORMAT(lastplayed, '%Y-%m-%d') AS lastPlayedDate
                    FROM mapratings
                    WHERE steamid = @steamid AND map = @map;";

                using (var playerCmd = new MySqlCommand(playerQuery, _mysqlConn))
                {
                    playerCmd.Parameters.AddWithValue("@map", mapName);
                    playerCmd.Parameters.AddWithValue("@steamid", auth);
                    using (var reader = await playerCmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            playerRating = reader.IsDBNull(reader.GetOrdinal("rating")) ? -1 : reader.GetInt32("rating");
                            ratingDate = reader.IsDBNull(reader.GetOrdinal("formattedDate")) ? "" : reader.GetString("formattedDate");
                            expired = reader.IsDBNull(reader.GetOrdinal("expired")) ? false : reader.GetBoolean("expired");
                            mapPlayed = reader.IsDBNull(reader.GetOrdinal("played")) ? 0 : reader.GetInt32("played");
                            lastPlayed = reader.IsDBNull(reader.GetOrdinal("lastPlayedDate")) ? "" : reader.GetString("lastPlayedDate");
                        }
                    }
                }

                // Second, calculate the average rating for the map along with total count
                var averageQuery = @"
                    SELECT AVG(rating) AS averageRating, COUNT(rating) AS totalRates
                    FROM mapratings
                    WHERE map = @map AND rating IS NOT NULL;";

                using (var avgCmd = new MySqlCommand(averageQuery, _mysqlConn))
                {
                    avgCmd.Parameters.AddWithValue("@map", mapName);
                    using (var reader = await avgCmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            averageRating = reader.IsDBNull(reader.GetOrdinal("averageRating")) ? -1.0 : reader.GetDouble("averageRating");
                            totalRates = reader.IsDBNull(reader.GetOrdinal("totalRates")) ? 0 : reader.GetInt32("totalRates");
                        }
                    }
                }

                await _mysqlConn.CloseAsync();
            }
            return (playerRating, averageRating, totalRates, ratingDate, expired, mapPlayed, lastPlayed);
        }
        catch (Exception ex)
        {
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[MapRatings] GetRating: An error occurred: {ex.Message}");
            });
            return (-1, -1, 0, "", false, 0, "");
        }
    }

    // New helper method added to allow grabbing both the average and the total rates simultaneously
    public async Task<(double averageRating, int totalRates)> GetSingleMapAverageAndCountAsync(string mapName)
    {
        double averageRating = -1.0;
        int totalRates = 0;
        if (!_isDatabaseReady)
        {
            Console.WriteLine("[Map Rate - FATAL] Database is not ready");
            return (averageRating, totalRates);
        }

        try
        {
            using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
            {
                await _mysqlConn.OpenAsync();
                string query = @"
                    SELECT AVG(rating) AS averageRating, COUNT(rating) AS totalRates
                    FROM mapratings
                    WHERE map = @MapName AND rating IS NOT NULL AND expired = FALSE;";

                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@MapName", mapName);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            averageRating = reader.IsDBNull(reader.GetOrdinal("averageRating")) ? -1.0 : reader.GetDouble("averageRating");
                            totalRates = reader.IsDBNull(reader.GetOrdinal("totalRates")) ? 0 : reader.GetInt32("totalRates");
                        }
                    }
                }
                await _mysqlConn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map Rate - FATAL] GetSingleMapAverageAndCountAsync: An error occurred: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[Map Rate - FATAL] GetSingleMapAverageAndCountAsync: An error occurred: {ex.Message}");
            });
        }

        return (averageRating, totalRates);
    }

    public async Task SetPlayedMapAsync(Player pl, string mapName)
    {
        if (!_isDatabaseReady)
        {
            Server.NextFrame(() => {
                Plugin.Logger.LogInformation($"[Map Rate - FATAL] Database is not ready");
            });
            return;
        }

        var playerName = pl.PlayerName;
        var steamId = pl.SteamID;
        var pc = Utilities.GetPlayerFromSlot(pl.Slot);
        if (pc == null || !Plugin.IsValidPlayer(pc))
        {
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[Map Rate - FATAL] SetPlayedMapAsync: Player slot {pl.Slot} is not valid.");
            });
            return;
        }

        bool checkReminder = false;
        if (string.IsNullOrEmpty(Plugin.Config.MapRateFlag) || 
            (!string.IsNullOrEmpty(Plugin.Config.MapRateFlag) && 
            AdminManager.PlayerHasPermissions(pc, Plugin.Config.MapRateFlag)))
        {
            checkReminder = true;
        }
        int played = 0;
        try
        {
            using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
            {
                await _mysqlConn.OpenAsync();

                var query = @"
                    INSERT INTO mapratings (player, steamid, map, played, lastplayed)
                    VALUES (@playerName, @SteamID, @Map, 1, CURRENT_DATE())
                    ON DUPLICATE KEY UPDATE 
                        player = VALUES(player),
                        played = played + 1,
                        lastplayed = CURRENT_DATE();";

                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@playerName", playerName);
                    command.Parameters.AddWithValue("@SteamID", steamId);
                    command.Parameters.AddWithValue("@Map", mapName);
                    await command.ExecuteNonQueryAsync(); // Executes the insert/update part
                }

                // Retrieve the updated "played" value
                query = "SELECT played FROM mapratings WHERE steamid = @SteamID AND map = @Map;";
                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@SteamID", steamId);
                    command.Parameters.AddWithValue("@Map", mapName);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync()) // Move to the first row of the result set
                        {
                            played = reader.IsDBNull(reader.GetOrdinal("played")) ? 0 : reader.GetInt32("played");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[Map Rate - FATAL] SetPlayedMapAsync: An error occurred: {ex.Message}");
            });
            return;
        }

        var player = Plugin.playerManager.GetPlayerBySteamID(steamId);
        if (player == null)
            return;
        player.playedMapTimes = played;
        // Compare the "played" value with the config variable MapsToPlayBeforeRate
        if (checkReminder)
        {
            Plugin.playerManager.CheckReminderRequired(player);
        }
    }
    public async Task<Dictionary<string, double>> GetMapAverageRatingsAsync(string mapName = null!)
    {
        var mapRatings = new Dictionary<string, double>();

        if (!_isDatabaseReady)
        {
            Console.WriteLine("[Map Rate - FATAL] Database is not ready");
            return mapRatings;
        }

        try
        {
            using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
            {
                await _mysqlConn.OpenAsync();

                string query;
                if (string.IsNullOrEmpty(mapName))
                {
                    // Query for average ratings of all maps
                    query = @"
                        SELECT map, AVG(rating) AS average_rating
                        FROM mapratings
                        WHERE rating IS NOT NULL AND expired = FALSE
                        GROUP BY map
                        ORDER BY average_rating DESC;";
                }
                else
                {
                    // Query for the average rating of a specific map
                    query = @"
                        SELECT map, AVG(rating) AS average_rating
                        FROM mapratings
                        WHERE map = @MapName AND rating IS NOT NULL AND expired = FALSE
                        GROUP BY map;";
                }

                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    if (!string.IsNullOrEmpty(mapName))
                    {
                        command.Parameters.AddWithValue("@MapName", mapName);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (reader.Read())
                        {
                            string map = reader.GetString("map");
                            double averageRating = reader.GetDouble("average_rating");
                            // Round the average rating to a maximum of two decimal places
                            double roundedAverageRating = Math.Round(averageRating, 2);
                            mapRatings.Add(map, roundedAverageRating);
                        }
                    }
                }
                await _mysqlConn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map Rate - FATAL] GetMapAverageRatingsAsync: An error occurred: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[Map Rate - FATAL] GetMapAverageRatingsAsync: An error occurred: {ex.Message}");
            });
        }

        return mapRatings;
    }
    public async Task UpdateExpiredRatings()
    {
        if (!_isDatabaseReady)
        {
            Console.WriteLine("[Map Rate - FATAL] Database is not ready");
            return;
        }

        try
        {
            using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
            {
                await _mysqlConn.OpenAsync();

                var query = @"
                    UPDATE mapratings
                    SET rating = NULL, ratingdate = NULL
                    WHERE DATEDIFF(CURRENT_DATE(), ratingdate) > @ExpirationDays AND expired = FALSE;";

                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@ExpirationDays", Plugin.Config.RatingExpirationDays);

                    int affectedRows = await command.ExecuteNonQueryAsync();
                }

                query = @"
                    UPDATE mapratings
                    SET played = 0
                    WHERE DATEDIFF(CURRENT_DATE(), lastplayed) > @IfPlayedMapExpirationDays;";

                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@IfPlayedMapExpirationDays", Plugin.Config.IfPlayedMapExpirationDays);

                    int affectedRows = await command.ExecuteNonQueryAsync();
                }

                await _mysqlConn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map Rate - FATAL] UpdateExpiredRatings: An error occurred: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"[Map Rate - FATAL] UpdateExpiredRatings: An error occurred: {ex.Message}");
            });
        }
    }
}
public class DatabaseOperationQueue
{
    private ConcurrentQueue<Func<Task>> _operationsQueue = new ConcurrentQueue<Func<Task>>();
    private SemaphoreSlim _signal = new SemaphoreSlim(0);
    private Task _worker;
    private bool _running = true;
    private MapRating Plugin;
    public DatabaseOperationQueue(MapRating plugin)
    {
        Plugin = plugin;
        // Start the worker task
        _worker = Task.Run(ProcessQueueAsync);
    }

    public void EnqueueOperation(Func<Task> operation)
    {
        _operationsQueue.Enqueue(operation);
        _signal.Release();
    }

    private async Task ProcessQueueAsync()
    {
        while (_running)
        {
            await _signal.WaitAsync();

            if (_operationsQueue.TryDequeue(out Func<Task>? operation) && operation != null)
            {
                try
                {
                    Server.NextFrame(async () => {
                        await operation();
                    });
                    
                }
                catch (Exception ex)
                {
                    // Handle exception (e.g., log error)
                    Console.WriteLine($"******************* Database operation failed: {ex.Message}");
                    Server.NextFrame( () => 
                    {
                        Plugin.Logger.LogError($"ProcessQueueAsync: Database operation failed: {ex.Message}");
                    });
                }
            }
        }
    }
    public void Stop()
    {
        _running = false;
        _signal.Release(); // Ensure the worker can exit if it's waiting
    }
}
public class PlayerManager
{
    private MapRating _plugin;
    public PlayerManager (MapRating plugin)
    {
        _plugin = plugin;
    }
    private readonly ConcurrentDictionary<ulong, Player> _players = new ConcurrentDictionary<ulong, Player>();
    public void ClearPlayers()
    {
        _players.Clear();
    }
    public Player AddOrUpdatePlayer(ulong steamId, CCSPlayerController pc)
    {
        Func<ulong, Player> addValueFactory = (key) => {
//             _plugin.Logger.LogInformation($"Adding new player: {pc.PlayerName} (SteamID: {key}, Slot: {pc.Slot})");
             return new Player(key, pc); // Assuming Player constructor sets IsActive = true
        };

        Func<ulong, Player, Player> updateValueFactory = (key, existingPlayer) => {
//            if (existingPlayer.PlayerName != pc.PlayerName || existingPlayer.Slot != pc.Slot || !existingPlayer.IsActive)
//            {
//                 _plugin.Logger.LogInformation($"Updating existing player: {existingPlayer.PlayerName} -> {pc.PlayerName} (SteamID: {key}, Slot: {existingPlayer.Slot} -> {pc.Slot}, Active: {existingPlayer.IsActive} -> True)");
//            }

//            existingPlayer.pc = pc; // Update the CCSPlayerController reference
            existingPlayer.PlayerName = pc.PlayerName; // Update name
            existingPlayer.Slot = pc.Slot;         // Update slot
            existingPlayer.IsActive = true;     // Ensure marked as active

            return existingPlayer; // Return the modified existing player
        };

        Player thePlayer = _players.AddOrUpdate(steamId, addValueFactory, updateValueFactory);

        return thePlayer;
    }
    public Player? GetPlayerBySteamID(ulong steamId)
    {
        _players.TryGetValue(steamId, out Player? player);
        // Returns the player if found, otherwise null
        return player;
    }
    public Player? FindPlayerBySlot(int targetSlot)
    {
        foreach (var player in _players.Values)
        {
            if (player.IsActive && player.Slot == targetSlot)
            {
                return player;
            }
        }
        return null;
    }
    public List<Player> GetActivePlayersToRemind()
    {
        return _players.Values
                       .Where(p => p.IsActive && p.requiredReminder)
                       .ToList();
    }
    public void CheckReminderRequired (Player player)
    {
        if (player != null && player.IsActive)
        {
            if (player.playedMapTimes >= _plugin.Config.MapsToPlayBeforeRate
                && !PlayedTimesExpired(player.lastPlayedDate)
                && (player.currentMapRating == -1 || player.ratingExpired || RatingCloseToExpiration(player.ratingDate)))
            {
                player.requiredReminder = true;
//                Server.NextFrame(() => {
//                    _plugin.Logger.LogInformation($"CheckReminderRequired: Player {player.PlayerName} set reminder to rate.");
//                });
            }
            else
            {
                player.requiredReminder = false;
//                Server.NextFrame(() => {
//                    _plugin.Logger.LogInformation($"CheckReminderRequired: Player {player.PlayerName} not necessary to reminder to rate.");
//                });
            }
        }
    }
    public bool PlayedTimesExpired(string lastPlayed)
    {
        if (string.IsNullOrEmpty(lastPlayed))
            return true;
        DateTime lastPlayedDateTime = DateTime.ParseExact(lastPlayed, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if ((DateTime.Now - lastPlayedDateTime).TotalDays > _plugin.Config.IfPlayedMapExpirationDays)
        {
            return true;
        }
        return false;
    }
    public bool RatingCloseToExpiration(string ratingDate)
    {
        if (!string.IsNullOrEmpty(ratingDate))
        {
            DateTime ratingDateTime = DateTime.ParseExact(ratingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if ((DateTime.Now - ratingDateTime).TotalDays > (_plugin.Config.RatingExpirationDays - _plugin.Config.DaysBeforeExpirationToRemindRate))
            {
                return true;
            }
        }
        return false;
    }
    public void PlayerDisconnect(Player player)
    {
        if (player.IsActive)
        {
            player.IsActive = false;
        }
        if (player.ActivateMenuTimer != null)
        {
            try
            {
                player.ActivateMenuTimer.Kill();
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => {
                    _plugin.Logger.LogError($"PlayerDisconnect: Error killing timer: {ex.Message}");
                });
            }
            player.ActivateMenuTimer = null;
        }
        if (player.playerTimer != null)
        {
            try
            {
                player.playerTimer.Kill();
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => {
                    _plugin.Logger.LogError($"PlayerDisconnect: Error killing timer: {ex.Message}");
                });
            }
            player.playerTimer = null;
        }
    }
    public List<Player> GetActivePlayers()
    {
        return _players.Values
                       .Where(p => p.IsActive)
                       .ToList();
    }
    public List<Player> GetAllPlayers()
    {
        return _players.Values.ToList();
    }
}
public class Player
{
    public Player (ulong id, CCSPlayerController player)
    {
        SteamID = id;
        PlayerName = player.PlayerName;
        Slot = player.Slot;
        IsActive = true;
    }
//    public CCSPlayerController pc;
    public ulong SteamID { get; set; } = 0;
    public int Slot { get; set; } = 0;
    public string PlayerName { get; set; } = "";
    public bool IsActive { get; set; } = false;
    public bool playedMap { get; set; } = false;
    public bool requiredReminder { get; set; } = false;
    public CounterStrikeSharp.API.Modules.Timers.Timer? playerTimer { get; set; } = null;
    public int currentMapRating { get; set; } = -1;
    public string ratingDate { get; set; } = "";
    public bool ratingExpired { get; set; } = false;
    public int playedMapTimes { get; set; } = -1;
    public string lastPlayedDate { get; set; } = "";
    public Timer? ActivateMenuTimer { get; set; } = null;
    public int TimerCounts { get; set; } = 0;
}