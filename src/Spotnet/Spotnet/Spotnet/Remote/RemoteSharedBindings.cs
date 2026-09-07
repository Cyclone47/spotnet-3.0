using System;
using Spotnet.Remote;

namespace Spotnet.Remote;

/// <summary>
/// Koppelt de gedeelde Spotnet.Remote-services (remote_config.json, UDP-ontdekking)
/// aan de Windows-instellingenmap en -versie zodra de Spotnet-assembly laadt —
/// een module-initializer, zodat elke instap (app-start, tests, DbDiagnostic)
/// automatisch de juiste binding krijgt en testvolgorde er niet meer toe doet.
///
/// De providers worden pas aangeroepen op het moment van een Load/Save/payload,
/// dus een test die AppHelper.SettingsFolder vervangt, blijft werken: het veld
/// wordt per aanroep opnieuw gelezen, precies zoals het oorspronkelijke lokale
/// RemoteConfig deed.
/// </summary>
public static class RemoteSharedBindings
{
    /// <summary>Door de module-initializer van deze assembly (EncodingSetup) aangeroepen;
    /// er mag maar één module-initializer per assembly zijn.</summary>
    internal static void Bind()
    {
        RemoteConfig.ConfigPathProvider = () =>
            System.IO.Path.Combine(Spotnet.Helpers.AppHelper.SettingsFolder ?? "", "remote_config.json");

        RemoteDiscoveryService.VersionProvider = () =>
            Spotnet.Helpers.AppHelper.AppVersion?.ToString() ?? "3.0";
    }
}
