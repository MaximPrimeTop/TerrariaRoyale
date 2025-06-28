using System.Timers;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using System.Text;
using Newtonsoft.Json;
using System.Globalization;
using TShockAPI.DB;
using Microsoft.Xna.Framework;
using TShockAPI.Hooks;
using Timer = System.Timers.Timer;

namespace TerrariaRoyale
{
    [ApiVersion(2, 1)]
    public class TerrariaRoyale : TerrariaPlugin
    {
        public static bool PvPEnabled = true;
        public enum GameMode
        {
            None,
            Grace,
            Normal,
            SuddenDeath
        }
        public static GameMode Mode = new();
        public override string Author => "MaximPrime";

        public override string Description => "Adds a battle royale gamemode for Terraria, built upon the GracePeriod plugin bu Kuz_";

        public override string Name => "Terraria Royale";

        public override Version Version => new Version(1, 0, 0, 0);

        public TerrariaRoyale(Main game) : base(game)
        {

        }

        private static Timer Timer = new();
        private static Timer OneSecTimer = new();
        private static string OneSecText;
        private static TimeSpan TimeLeft;
        private static int grace_time;
        private static int sudden_death_time;
        private static Random rnd = new();
        private static List<TSPlayer> SuddenDeathPlayers = new();

        private static Config config;
        internal static string filepath { get { return Path.Combine(TShock.SavePath, "grace.json"); } }


        private static void ReadConfig<TConfig>(string path, TConfig defaultConfig, out TConfig config)
        {
            if (!File.Exists(path))
            {
                config = defaultConfig;
                File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            else
            {
                config = JsonConvert.DeserializeObject<TConfig>(File.ReadAllText(path));

            }
        }

        public override void Initialize()
        {
            ReadConfig(filepath, Config.DefaultConfig(), out config);
            if (config.announcement_color == "_")
            {
                config = Config.DefaultConfig();
                File.WriteAllText(filepath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            GeneralHooks.ReloadEvent += OnServerReload;

            GetDataHandlers.TogglePvp += OnTogglePvp;

            Commands.ChatCommands.Add(new Command("tshock.grace", Grace, "grace"));

            Mode = GameMode.None;
        }

        private void OnServerReload(ReloadEventArgs args)
        {
            var player = args.Player;
            try
            {
                ReadConfig(filepath, Config.DefaultConfig(), out config);
                player.SendSuccessMessage("[Terraria Royale] Config reloaded!");
            }
            catch (Exception ex)
            {
                player.SendErrorMessage("[Terraria Royale] Error reading config file: {0}", ex.Message);
                config = Config.DefaultConfig();
                File.WriteAllText(filepath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
        }

        private void OnGameUpdate(EventArgs args)
        {
            foreach (TSPlayer player in TShock.Players)
            {
                if (player == null) continue;
                if (player.TPlayer.hostile != PvPEnabled)
                {
                    player.SetPvP(PvPEnabled);
                    player.SendInfoMessage("Your PvP was {0}abled!", PvPEnabled ? "en" : "dis");
                }
            }
        }

        private void Grace(CommandArgs args)
        {
            TSPlayer player = args.Player;
            if (args.Parameters.Count == 0)
            {
                player.SendErrorMessage("Invalid syntax! Check {0}grace help to see available commands", Commands.Specifier);
                return;
            }
            switch (args.Parameters[0])
            {
                case "start":
                    if (args.Parameters.Count == 3)
                    {
                        if (int.TryParse(args.Parameters[1], out grace_time) && int.TryParse(args.Parameters[2], out sudden_death_time))
                            GraceStart();
                        else
                            player.SendErrorMessage("Invalid syntax! Proper syntax: {0}grace start <time in seconds> <time in seconds>", Commands.Specifier);
                    }
                    else
                        player.SendErrorMessage("Invalid syntax! Proper syntax: {0}grace start <time in seconds> <time in seconds>", Commands.Specifier);
                    break;
                case "stop":
                    GraceStop(args.Player);
                    break;
                case "help":
                    GraceHelp(args.Player);
                    break;
                default:
                    player.SendErrorMessage("Invalid syntax! Check {0}grace help to see available commands", Commands.Specifier);
                    break;
            }
        }

        private void GraceStart()
        {
            Mode = GameMode.Grace;
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            ResetTimer(ref Timer, GraceStopEvent, grace_time * 1000, false);
            ResetTimer(ref OneSecTimer, OnOneSec, 1000);
            TimeLeft = TimeSpan.FromSeconds(grace_time);
            PvPEnabled = false;
            OneSecText = RepeatLineBreaks(12) + "[i:4083] [c/" + config.text_color + ":Grace period ends: ]\n" + "[c/" + config.timer_color + ":";
        }

        private void GraceHelp(TSPlayer player)
        {
            player.SendInfoMessage("/grace help - see this message");
            player.SendInfoMessage("/grace start <timeInSeconds> <timeInSeconds> - start the grace period and the sudden death period, set it to -1 to make it infinite");
            player.SendInfoMessage("/grace stop - immediately stop the grace period and force PvP on");
        }

        private void GraceStop(TSPlayer Player)
        {
            switch (Mode)
            {
                case GameMode.Grace:
                    PvPEnabled = true;
                    TShock.Utils.Broadcast(config.grace_end_text, HexToColor(config.announcement_color));
                    foreach (TSPlayer player in TShock.Players)
                        if (player != null)
                            player.SendData(PacketTypes.Status, number2: 1);
                    ResetTimer(ref Timer, SuddenDeathEvent, sudden_death_time * 1000, false);
                    TimeLeft = TimeSpan.FromSeconds(sudden_death_time);
                    OneSecText = RepeatLineBreaks(12) + "[i:3349] [c/" + config.text_color + ":Sudden death starts: ]\n" + "[c/" + config.timer_color + ":";
                    Mode = GameMode.Normal;
                    break;
                case GameMode.Normal:
                    foreach (TSPlayer player in TShock.Players)
                        if (player != null)
                            player.SendData(PacketTypes.Status, number2: 1);

                    TShock.Utils.Broadcast(config.sudden_death_text, HexToColor(config.announcement_color));
                    Mode = GameMode.SuddenDeath;
                    PvPEnabled = false;
                    TimeLeft = TimeSpan.FromSeconds(6);
                    Timer.Stop();
                    Timer.Dispose();
                    ResetTimer(ref OneSecTimer, SuddenDeathGraceTime, 1000);
                    SuddenDeathPlayers = TShock.Players.Where(p => p != null && p.Active && p.TPlayer.active).ToList();
                    ServerApi.Hooks.NetGetData.Register(this, OnGetData);
                    TPAll();
                    break;
                case GameMode.SuddenDeath:
                    break;
                default:
                    Player.SendErrorMessage("You can't stop the grace period because it hasn't started yet!");
                    break;
            }
        }

        internal void GraceStopEvent(object sender, ElapsedEventArgs args)
        {
            PvPEnabled = true;
            TShock.Utils.Broadcast(config.grace_end_text, HexToColor(config.announcement_color));
            ResetTimer(ref Timer, SuddenDeathEvent, sudden_death_time * 1000, false);
            TimeLeft = TimeSpan.FromSeconds(sudden_death_time);
            OneSecText = RepeatLineBreaks(12) + "[i:4144] [c/" + config.text_color + ":Sudden death starts: ]\n" + "[c/" + config.timer_color + ":";
            Mode = GameMode.Normal;
        }

        internal void SuddenDeathEvent(object sender, ElapsedEventArgs args)
        {
            foreach (TSPlayer player in TShock.Players)
                if (player != null)
                    player.SendData(PacketTypes.Status, number2: 1);

            TShock.Utils.Broadcast(config.sudden_death_text, HexToColor(config.announcement_color));
            Mode = GameMode.SuddenDeath;
            PvPEnabled = false;
            TimeLeft = TimeSpan.FromSeconds(6);
            Timer.Stop();
            Timer.Dispose();
            ResetTimer(ref OneSecTimer, SuddenDeathGraceTime, 1000);
            SuddenDeathPlayers = TShock.Players.Where(p => p != null && p.Active && p.TPlayer.active).ToList();
            ServerApi.Hooks.NetGetData.Register(this, OnGetData);
            TPAll();
        }

        internal void SuddenDeathGraceTime(object sender, ElapsedEventArgs args)
        {
            TimeLeft -= TimeSpan.FromSeconds(1);
            if (TimeLeft.Seconds > 0)
            {
                TShock.Utils.Broadcast($"Sudden death will start in {TimeLeft.Seconds}!", HexToColor(config.timer_color));
                return;
            }
            OneSecTimer.Stop();
            OneSecTimer.Dispose();
            SuddenDeath();
        }

        private void SuddenDeath()
        {
            TShock.Utils.Broadcast("FIGHTTTTTTT!!!!", HexToColor(config.announcement_color));
            PvPEnabled = true;
        }

        private void OnGetData(GetDataEventArgs args)
        {

            if (args.Handled || args.MsgID != PacketTypes.PlayerDeathV2 || Mode != GameMode.SuddenDeath) return;

            var player = TShock.Players[args.Msg.whoAmI];
            if (player == null || !SuddenDeathPlayers.Contains(player)) return;
            TSPlayer winner = null;
            if (SuddenDeathPlayers.Count == 2)
                winner = SuddenDeathPlayers.FirstOrDefault(p => p != player);

            SuddenDeathPlayers.Remove(player);

            if (winner != null && SuddenDeathPlayers.Count == 1)
            {
                TShock.Utils.Broadcast($"The winner is {winner.Name}!", HexToColor(config.timer_color));
                Mode = GameMode.None;
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
            }
        }

        internal static void OnOneSec(object sender, ElapsedEventArgs args)
        {
            TimeLeft -= TimeSpan.FromSeconds(1);
            string time_left;
            if (TimeLeft.Hours != 0)
                time_left = TimeLeft.Hours.ToString() + "h " + TimeLeft.Minutes.ToString() + "m " + TimeLeft.Seconds.ToString() + "s ";
            else if (TimeLeft.Minutes != 0)
                time_left = TimeLeft.Minutes.ToString() + "m " + TimeLeft.Seconds.ToString() + "s ";
            else
                time_left = TimeLeft.Seconds.ToString() + "s ";
            foreach (TSPlayer player in TShock.Players)
                player.SendData(PacketTypes.Status, OneSecText + time_left + "]", number2: 1);
        }

        private void OnTogglePvp(object sender, GetDataHandlers.TogglePvpEventArgs args)
        {
            if (Mode == GameMode.None) return;
            if (args.Pvp == PvPEnabled) return;

            TSPlayer player = args.Player;
            player.SetPvP(PvPEnabled);
            player.SendErrorMessage("You're not allowed to toggle PvP!");
            args.Handled = true;
        }

        public static string RepeatLineBreaks(int number)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < number; i++)
                sb.Append("\r\n");

            return sb.ToString();
        }

        private static void TPAll()
        {
            int index = rnd.Next(config.valid_warps.Count);
            Warp warp = TShock.Warps.Find(config.valid_warps[index]);
            if (warp == null)
            {
                TShock.Utils.Broadcast($"No valid warps found with the name {config.valid_warps[index]}! Please check your config file.", Microsoft.Xna.Framework.Color.Red);
                return;
            }
            foreach (TSPlayer plr in TShock.Players)
                plr.Teleport(warp.Position.X * 16, warp.Position.Y * 16);
        }

        public static Color HexToColor(string hex)
        {
            if (hex.Length != 6)
                throw new ArgumentException("Hex color must be 6 characters long.");
            int r = int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier);
            int g = int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier);
            int b = int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier);
            return new Color(r, g, b);
        }

        public static void ResetTimer(ref Timer timer, ElapsedEventHandler handler, double interval, bool autoReset = true)
        {
            timer.Stop();
            timer.Dispose();
            timer = new Timer(interval)
            {
                AutoReset = autoReset,
                Enabled = true
            };
            timer.Elapsed += handler;
        }
    }
}
