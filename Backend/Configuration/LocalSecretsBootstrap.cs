namespace Backend.Configuration;

/// <summary>
/// In Development, loads optional <c>.env</c> files and maps <c>SMAPPEE_*</c> variables to ASP.NET configuration keys
/// without relying on direnv. Prefer <c>Backend/.env</c> or repo-root <c>.env</c> for Smappee (see <c>smappee.env.example</c>).
/// <see cref="MapSmappeeAliases"/> also runs in non-Development (e.g. Docker) so the same <c>SMAPPEE_*</c> names work when set via <c>env_file</c>.
/// </summary>
public static class LocalSecretsBootstrap
{
    public static bool IsDevelopmentEnvironment()
    {
        var e = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.Equals(e, "Development", StringComparison.OrdinalIgnoreCase);
    }

    public static void Apply()
    {
        foreach (var path in EnumerateDotEnvCandidatePaths())
        {
            if (File.Exists(path))
                LoadDotEnvFile(path);
        }

        MapSmappeeAliases();
    }

    /// <summary>
    /// Maps <c>SMAPPEE_CLIENT_ID</c> (etc.) to <c>Smappee__*</c> so <see cref="Smappee.SmappeeOptions"/> binds. Safe to call when
    /// <c>Smappee__*</c> is already set (e.g. production compose). Skips a target if it is non-empty; empty string is treated as unset.
    /// </summary>
    public static void MapSmappeeAliases() => MapSmappeeAliasesCore();

    private static void MapSmappeeAliasesCore()
    {
        void Alias(string configurationKey, string legacyKey)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(configurationKey)))
                return;
            var v = Environment.GetEnvironmentVariable(legacyKey);
            if (!string.IsNullOrEmpty(v))
                Environment.SetEnvironmentVariable(configurationKey, v);
        }

        Alias("Smappee__ClientId", "SMAPPEE_CLIENT_ID");
        Alias("Smappee__ClientSecret", "SMAPPEE_CLIENT_SECRET");
        Alias("Smappee__Username", "SMAPPEE_USERNAME");
        Alias("Smappee__Password", "SMAPPEE_PASSWORD");

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Smappee__BaseUrl")))
        {
            var baseUrl = Environment.GetEnvironmentVariable("SMAPPEE_BASE_URL");
            var ver = Environment.GetEnvironmentVariable("SMAPPEE_API_VERSION");
            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(ver))
            {
                var combined = $"{baseUrl.TrimEnd('/')}/dev/{ver.Trim('/')}";
                Environment.SetEnvironmentVariable("Smappee__BaseUrl", combined);
            }
        }

        Alias("Smappee__ServiceLocationId", "SMAPPEE_SERVICE_LOCATION_ID");

        Alias("Sigenergy__BaseUrl", "SIGENERGY_BASE_URL");
        Alias("Sigenergy__Username", "SIGENERGY_USERNAME");
        Alias("Sigenergy__Password", "SIGENERGY_PASSWORD");
        Alias("Sigenergy__SystemId", "SIGENERGY_SYSTEM_ID");
        Alias("Sigenergy__UsableBatteryCapacityKwh", "SIGENERGY_USABLE_BATTERY_CAPACITY_KWH");

        // If only one side is set, mirror it to the other so backend->daemon calls work by default.
        Alias("IntegrationDaemon__ApiKey", "DaemonApi__ApiKey");
        Alias("DaemonApi__ApiKey", "IntegrationDaemon__ApiKey");
    }

    private static IEnumerable<string> EnumerateDotEnvCandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cwd = Directory.GetCurrentDirectory();
        var list = new List<string>();
        void TryAdd(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                var full = Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(cwd, p));
                if (seen.Add(full))
                    list.Add(full);
            }
            catch
            {
                // ignore
            }
        }

        var dir = cwd;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            TryAdd(Path.Combine(dir, ".env"));
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        TryAdd(Path.Combine(cwd, "Backend", ".env"));

        return list;
    }

    private static void LoadDotEnvFile(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            if (key.Length == 0) continue;

            var val = line[(eq + 1)..].Trim();
            if (val.Length >= 2 &&
                ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
                val = val[1..^1];

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                continue;

            Environment.SetEnvironmentVariable(key, val);
        }
    }

}
