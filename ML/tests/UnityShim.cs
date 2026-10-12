using System;
using System.IO;

namespace UnityEngine
{
    /// <summary>The one Unity call the Edit-mode tests make: where the game's data lies.</summary>
    public static class Application
    {
        public static string streamingAssetsPath
        {
            get
            {
                // Up from the test's folder to the repository, then into the game.
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "Game", "TinyTribe", "Assets"))) folder = folder.Parent;
                if (folder == null) throw new DirectoryNotFoundException("Game/TinyTribe/Assets not found above " + AppContext.BaseDirectory);
                return Path.Combine(folder.FullName, "Game", "TinyTribe", "Assets", "StreamingAssets");
            }
        }
    }
}
