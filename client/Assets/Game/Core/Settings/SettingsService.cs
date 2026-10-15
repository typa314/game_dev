using System;
using System.IO;
using Game.Domain;
using Newtonsoft.Json;

namespace Game.Core.Settings
{
    public sealed class SettingsService
    {
        private readonly string _settingsFilePath;
        private GameSettings _currentSettings;

        public GameSettings Current => _currentSettings ?? (_currentSettings = new GameSettings());

        public SettingsService(string settingsFilePath = null)
        {
            _settingsFilePath = settingsFilePath;
        }

        public GameSettings Load()
        {
            if (string.IsNullOrEmpty(_settingsFilePath) || !File.Exists(_settingsFilePath))
            {
                _currentSettings = new GameSettings();
                return _currentSettings;
            }

            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                _currentSettings = JsonConvert.DeserializeObject<GameSettings>(json) ?? new GameSettings();
            }
            catch
            {
                _currentSettings = new GameSettings();
            }

            return _currentSettings;
        }

        public void Save(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _currentSettings = settings.Clone();

            if (string.IsNullOrEmpty(_settingsFilePath)) return;

            string dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonConvert.SerializeObject(_currentSettings, Formatting.Indented);
            File.WriteAllText(_settingsFilePath, json);
        }

        /// <summary>
        /// D4-2: 分級選擇不能高於建置版本上限（BuildProfile.MaxRating）。
        /// </summary>
        public static Rating GetClampedRating(GameSettings settings, Rating maxRating)
        {
            if (settings == null) return Rating.G;
            return settings.RatingChoice > maxRating ? maxRating : settings.RatingChoice;
        }
    }
}
