using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Xml;

namespace Spotnet.Deployment;

/// <summary>Stable settings location across executable paths and assembly versions.</summary>
internal sealed class InstalledSettingsProvider : SettingsProvider
{
    private readonly string _path;
    internal InstalledSettingsProvider(string path) { _path = path; }
    public override string ApplicationName { get; set; } = "Spotnet3";
    public override void Initialize(string name, NameValueCollection config) => base.Initialize("Spotnet3Profile", config);

    private XmlDocument Read() => File.Exists(_path) ? ProfileSettingsFile.Normalize(ProfileSettingsFile.Load(_path)) : ProfileSettingsFile.Empty();

    public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
    {
        var document = Read(); // Corrupt settings fail visibly; do not silently overwrite them.
        var section = ProfileSettingsFile.Section(document);
        var result = new SettingsPropertyValueCollection();
        foreach (SettingsProperty property in properties)
        {
            XmlConvert.VerifyNCName(property.Name);
            var value = section.SelectSingleNode("setting[@name='" + property.Name + "']/value");
            result.Add(new SettingsPropertyValue(property)
            {
                SerializedValue = value == null ? property.DefaultValue :
                    property.SerializeAs == SettingsSerializeAs.Xml ? value.InnerXml : value.InnerText,
                IsDirty = false
            });
        }
        return result;
    }

    public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values)
    {
        var document = Read();
        foreach (SettingsPropertyValue value in values)
        {
            if (value.Property.SerializeAs != SettingsSerializeAs.String)
                throw new ConfigurationErrorsException("Unsupported installed-profile serialization: " + value.Name);
            ProfileSettingsFile.Set(document, value.Name, Convert.ToString(value.SerializedValue, System.Globalization.CultureInfo.InvariantCulture));
        }
        ProfileSettingsFile.SaveAtomic(document, _path);
    }
}
