namespace Game.Core.Platform
{
    /// <summary>
    /// 空平台實作。用於無 Steam 或單元測試環境，所有操作均安全回傳預設值。
    /// </summary>
    public sealed class NullPlatformService : IPlatformService
    {
        public bool SyncEnabled { get; set; } = false;

        public bool IsDlcInstalled(string dlcId) => false;

        public bool UnlockAchievement(string steamApiName) => true;

        public string GetLanguage() => "tchinese";
    }
}
