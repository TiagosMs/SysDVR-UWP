using SysDVR.Client.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SysDVR.Client.Platform
{
	internal static class Resources
	{
		static string BasePath => AppContext.BaseDirectory;
		static string ResourcePath(string x) => Path.Combine(BasePath, "Assets", x);

		public static string SettingsStorePath()
		{
			try
			{
				return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
			}
			catch
			{
				return AppContext.BaseDirectory;
			}
		}

		public static byte[] ReadResource(string path) => File.ReadAllBytes(path);
		public static bool ResourceExists(string path) => File.Exists(path);

		public static bool HasDiskAccessPermission() => true;
		public static bool CanRequestDiskAccessPermission() => true;
		public static void RequestDiskAccessPermission() { }

		static string[] GetTranslationFiles()
		{
			var dir = ResourcePath("strings");
			if (!Directory.Exists(dir))
				return Array.Empty<string>();

			return Directory.GetFiles(dir, "*.json");
		}

		public static string RuntimesFolder => BasePath;
		public static string MainFont { get; private set; } = ResourcePath("fonts/OpenSans.ttf");
		public static string LoadingImage => ResourcePath("loading.yuv");

		public readonly static string Logo = ResourcePath("logo.png");
		public readonly static string UsbIcon = ResourcePath("ico_usb.png");
		public readonly static string WifiIcon = ResourcePath("ico_wifi.png");

		public static StringTableMetadata[] GetAvailableTranslations()
		{
			var files = GetTranslationFiles();
			var result = new List<StringTableMetadata>();

			foreach (var file in files)
			{
				try
				{
					var table = JsonSerializer.Deserialize<StringTableMetadata>(ReadResource(file), StringTableSerializer.Default.SysDVRStringTableMetadata);
					if (table is null)
						continue;

					if (table.SystemLocale.Length == 0)
					{
						Debug.WriteLine($"Translation {table.TranslationName} was not loaded due to a missing system locale");
						continue;
					}

					table.FileName = file;
					result.Add(table);
				}
				catch (Exception ex)
				{
					Program.DebugLog($"Failed to load translation {file} : {ex}");    
				}
			}

			return result.ToArray();
		}

		public static StringTable? LoadTranslationFromAssetName(string fullAssetName)
		{
			return JsonSerializer.Deserialize<StringTable>(ReadResource(fullAssetName), StringTableSerializer.Default.SysDVRStringTable) 
				?? throw new Exception($"Failed to deserialize {fullAssetName}");
		}

		public static bool OverrideMainFont(string fontName)
		{
			var font = ResourcePath(fontName);
			if (!ResourceExists(font))
			{
				return false;
			}

			MainFont = font;
			return true;
		}
	}
}
