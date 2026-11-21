// Tupic SDK core: where the Tupic services are. A build talks to exactly one environment; the local one is the
// default, and a production or development host must be chosen explicitly, never by accident.
using System;
using System.Collections.Generic;

namespace Tupic.Core
{
    public sealed class TupicEnvironment
    {
        public string Name;
        /// <summary>Keycloak realm issuer, e.g. https://iam.tupic.app/realms/tupic.</summary>
        public string Issuer;
        /// <summary>Public OIDC client of the game (authorization code + PKCE, loopback redirect).</summary>
        public string ClientId = "games-unity";
        /// <summary>games-core API root: games, scores, analytics, Live bridge (/games/api/v1).</summary>
        public string GamesApi;
        /// <summary>assets-core API root (/assets/v1).</summary>
        public string AssetsApi;
        /// <summary>insights-core API root (/insights/v1).</summary>
        public string InsightsApi;
        /// <summary>chain-core API root (/chain/api); the SDK only reads from it.</summary>
        public string ChainApi;
        /// <summary>Only for a local stack: allows the resource-owner password grant used by automated tests.</summary>
        public bool AllowPasswordGrant;

        public string TokenEndpoint => Issuer + "/protocol/openid-connect/token";
        public string AuthorizeEndpoint => Issuer + "/protocol/openid-connect/auth";

        static readonly string[] RemoteSuffixes = { ".tupic.com", ".tupic.dev", ".tupic.org" };

        /// <summary>The laptop stack: *.tupic.app resolves to 127.0.0.1 through the hosts file (local-env nginx).</summary>
        public static TupicEnvironment Local(string clientId = "games-unity") => new TupicEnvironment
        {
            Name = "local",
            Issuer = "https://iam.tupic.app/realms/tupic",
            ClientId = clientId,
            GamesApi = "https://api.tupic.app/games/api/v1",
            AssetsApi = "https://api.tupic.app/assets/v1",
            InsightsApi = "https://api.tupic.app/insights/v1",
            ChainApi = "https://api.tupic.app/chain/api",
            AllowPasswordGrant = true,
        };

        /// <summary>A remote environment. Requires allowRemote: true, so a test or a sample never reaches it by default.</summary>
        public static TupicEnvironment Remote(string name, string domain, bool allowRemote, string clientId = "games-unity")
        {
            if (!allowRemote) throw new InvalidOperationException($"Remote environment '{name}' needs allowRemote: true.");
            return new TupicEnvironment
            {
                Name = name,
                Issuer = $"https://iam.{domain}/realms/tupic",
                ClientId = clientId,
                GamesApi = $"https://api.{domain}/games/api/v1",
                AssetsApi = $"https://api.{domain}/assets/v1",
                InsightsApi = $"https://api.{domain}/insights/v1",
                ChainApi = $"https://api.{domain}/chain/api",
            };
        }

        /// <summary>Throws when a local environment points at a remote Tupic host (a typo or a copied config).</summary>
        public void Validate()
        {
            foreach (var url in new[] { Issuer, GamesApi, AssetsApi, InsightsApi, ChainApi })
            {
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                    (u.Scheme != "https" && !(u.Scheme == "http" && u.IsLoopback)))
                    throw new InvalidOperationException($"Environment '{Name}': invalid service URL '{url}'.");
                if (Name == "local")
                    foreach (var suffix in RemoteSuffixes)
                        if (u.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Local environment points at remote host {u.Host}.");
            }
            if (AllowPasswordGrant && Name != "local")
                throw new InvalidOperationException("The password grant is only allowed against the local stack.");
        }

        public IEnumerable<string> Hosts()
        {
            foreach (var url in new[] { Issuer, GamesApi, AssetsApi, InsightsApi, ChainApi }) yield return new Uri(url).Host;
        }
    }
}
