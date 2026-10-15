namespace Game.Core.Platform
{
    /// <summary>
    /// 平台服務介面（Steam / Standalone / Null，見 design/02 ADR-005、design/17 §D2）。
    /// </summary>
    public interface IPlatformService
    {
        bool SyncEnabled { get; set; }
        bool IsDlcInstalled(string dlcId);
        bool UnlockAchievement(string steamApiName);
        string GetLanguage();
    }
}
