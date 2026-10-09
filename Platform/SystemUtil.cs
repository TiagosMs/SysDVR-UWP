using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SysDVR.Client.Platform
{
	internal static class SystemUtil
	{
		public static bool OpenURL(string url)
		{
			try
			{
				var uri = new Uri(url);
				_ = Windows.System.Launcher.LaunchUriAsync(uri);
				return true;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"OpenURL failed: {ex.Message}");
				return false;
			}
		}

		const string SettingsFileName = "options.json";

		public static string? LoadSettingsString()
		{
			var path = Resources.SettingsStorePath();

			if (string.IsNullOrWhiteSpace(path))
				return null;

			var file = Path.Combine(path, SettingsFileName);
			if (File.Exists(file))
				return File.ReadAllText(file);

			return null;
		}

		public static void StoreSettingsString(string json)
		{
			var path = Resources.SettingsStorePath();
			if (string.IsNullOrWhiteSpace(path))
				return;

			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);

			var file = Path.Combine(path, SettingsFileName);
			File.WriteAllText(file, json);
		}

		public static string GetLanguageCode() 
		{
			return CultureInfo.CurrentUICulture.Name;
		}
	}
}
