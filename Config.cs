namespace TerrariaRoyale
{
    public class Config
    {
        public string text_color = "";
        public string timer_color = "";
        public string announcement_color = "_";
        public string grace_end_text = "";
        public string sudden_death_text = "";
        public List<string> valid_warps = new();

        public static Config DefaultConfig()
        {
            Config vConf = new Config
            {
                text_color = "FFFFFF",
                timer_color = "8eec8e",
                announcement_color = "FF0000",
                grace_end_text = "The grace period has ended.\nYou are now at the mercy of yourself and others.\nYou are being forced into PvP.",
                sudden_death_text = "Sudden death!!!",
                valid_warps = new()
            };

            return vConf;
        }
    }
}
