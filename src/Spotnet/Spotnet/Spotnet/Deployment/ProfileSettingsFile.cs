using System;
using System.IO;
using System.Xml;

namespace Spotnet.Deployment;

/// <summary>Data-only settings import. Never instantiate types named by a legacy config.</summary>
public static class ProfileSettingsFile
{
    public static XmlDocument Load(string path)
    {
        using (var input = File.OpenRead(path)) return Load(input);
    }

    public static XmlDocument Load(Stream input)
    {
        var document = new XmlDocument { XmlResolver = null };
        var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 };
        using (var reader = XmlReader.Create(input, options)) document.Load(reader);
        return document;
    }

    public static XmlDocument Empty()
    {
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml("<configuration><userSettings><Spotnet.Properties.Settings /></userSettings></configuration>");
        return document;
    }

    public static XmlElement Section(XmlDocument document) =>
        document.SelectSingleNode("/configuration/userSettings/Spotnet.Properties.Settings") as XmlElement;

    public static XmlDocument Normalize(XmlDocument source)
    {
        var result = Empty();
        var target = Section(result);
        var original = Section(source);
        if (original != null)
        {
            foreach (XmlNode node in original.ChildNodes)
            {
                if (!(node is XmlElement setting) || setting.Name != "setting") continue;
                var name = setting.GetAttribute("name");
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '\'', '"', '[', ']' }) >= 0)
                    throw new InvalidDataException("Invalid setting name in the selected configuration.");
                var value = setting["value"];
                if (value == null) continue;
                if (target.SelectSingleNode("setting[@name='" + name + "']") != null)
                    throw new InvalidDataException("Duplicate setting in the selected configuration.");
                var copy = result.CreateElement("setting");
                copy.SetAttribute("name", name);
                copy.SetAttribute("serializeAs", setting.GetAttribute("serializeAs"));
                copy.AppendChild(result.ImportNode(value, true));
                target.AppendChild(copy);
            }
        }
        else if (source.DocumentElement?.Name == "Settings")
        {
            foreach (XmlNode node in source.DocumentElement.ChildNodes)
            {
                if (node is XmlElement element) Set(result, element.Name, element.InnerText);
            }
        }
        else throw new InvalidDataException("Not a Spotnet 2.x/3.x settings file. Spotnet 1.x settings require manual conversion.");
        return result;
    }

    public static string Get(XmlDocument document, string name)
    {
        XmlConvert.VerifyNCName(name);
        return (Section(document)?.SelectSingleNode("setting[@name='" + name + "']/value"))?.InnerText;
    }

    /// <summary>Seeds a value the installer knows without overruling one the imported profile carried.</summary>
    public static void SetIfAbsent(XmlDocument document, string name, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!string.IsNullOrWhiteSpace(Get(document, name))) return;
        Set(document, name, value);
    }

    public static void Set(XmlDocument document, string name, string value)
    {
        XmlConvert.VerifyNCName(name);
        var section = Section(document) ?? throw new InvalidDataException("Missing Spotnet settings section.");
        var setting = section.SelectSingleNode("setting[@name='" + name + "']") as XmlElement;
        if (setting == null)
        {
            setting = document.CreateElement("setting");
            setting.SetAttribute("name", name);
            section.AppendChild(setting);
        }
        setting.SetAttribute("serializeAs", "String");
        var element = setting["value"] ?? (XmlElement)setting.AppendChild(document.CreateElement("value"));
        element.InnerText = value ?? string.Empty;
    }

    public static void SaveAtomic(XmlDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            document.Save(stream);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".previous", true);
        else File.Move(temporary, path);
    }
}
