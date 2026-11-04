using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlPrompter.Core.Settings
{
    /// <summary>Reading and writing the settings file (UTF-8 JSON, indented, no external dependencies).</summary>
    public static class SettingsJson
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(PrompterSettings));

        public static string Serialize(PrompterSettings settings)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    Serializer.WriteObject(writer, settings);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <exception cref="FormatException">The text is not valid settings JSON.</exception>
        public static PrompterSettings Deserialize(string json)
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
                {
                    return (PrompterSettings)Serializer.ReadObject(stream) ?? new PrompterSettings();
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is ArgumentException)
            {
                throw new FormatException("The settings file is not valid JSON: " + ex.Message, ex);
            }
        }

        /// <summary>Loads the file; a missing file gives the defaults.</summary>
        /// <exception cref="FormatException">The file is not valid settings JSON.</exception>
        public static PrompterSettings Load(string path) =>
            File.Exists(path) ? Deserialize(File.ReadAllText(path, Encoding.UTF8)) : new PrompterSettings();

        /// <summary>Writes through a temporary file so that a crash never leaves a half-written file.</summary>
        public static void Save(string path, PrompterSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }
    }
}
