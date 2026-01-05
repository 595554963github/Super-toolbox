using System.Text.Json;

namespace super_toolbox
{
    public class Preferences
    {
        private const string ConfigFileName = "super_toolbox_config.json";

        public Dictionary<string, bool> ExpandedCategories { get; set; } = new Dictionary<string, bool>();

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, json);
            }
            catch (Exception)
            {
            }
        }
        public static Preferences Load()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    return JsonSerializer.Deserialize<Preferences>(json) ?? new Preferences();
                }
            }
            catch (Exception)
            {
            }
            return new Preferences();
        }
    }
}