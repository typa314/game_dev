using System;

namespace Game.Core.Story
{
    /// <summary>
    /// 角色路線劇本檔名。只使用該角色自己的 storyRoute 或 charNN 前綴，不借用其他角色的劇本。
    /// </summary>
    public static class RouteScriptNames
    {
        public static string FileName(string charId, int chapter, string storyRoute)
        {
            string routeBase = storyRoute;
            if (string.IsNullOrEmpty(routeBase))
                routeBase = "route_" + Prefix(charId);
            return routeBase + "_ch" + chapter + ".txt";
        }

        public static string Prefix(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return charId;
            int underscore = charId.IndexOf('_');
            return underscore > 0 ? charId.Substring(0, underscore) : charId;
        }
    }
}
