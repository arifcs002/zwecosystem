namespace Ecommerce.Api.Infrastructure
{
    // Refuses to start a PRODUCTION server with a configuration that would be unsafe, instead of running quietly
    // with a guessable signing key. Development and the test suite are never blocked.
    public static class StartupChecks
    {
        private static readonly string[] WeakMarkers =
            { "changeme", "change-me", "change_me", "your-secret", "your_secret", "secret-key-here", "example", "ci-only", "password", "12345678", "00000000" };

        public static (List<string> Fatal, List<string> Warnings) Evaluate(string environment, string? jwtSecret, string? connectionString, string? redis)
        {
            var fatal = new List<string>(); var warn = new List<string>();
            var isProd = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
            var secret = jwtSecret ?? "";
            if (isProd)
            {
                if (secret.Length < 32) fatal.Add($"JWT_SECRET is only {secret.Length} characters; at least 32 are required. Anyone could forge logins with a short key.");
                else if (WeakMarkers.Any(m => secret.Contains(m, StringComparison.OrdinalIgnoreCase))) fatal.Add("JWT_SECRET looks like a placeholder or test value. Generate a long random one (e.g. `openssl rand -base64 48`).");
                else if (secret.Distinct().Count() < 8) fatal.Add("JWT_SECRET has too little variety to be random.");
            }
            var host = (connectionString ?? "").Split(';').Select(s => s.Trim()).FirstOrDefault(s => s.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))?.Substring(5) ?? "";
            if (isProd && (host == "localhost" || host == "127.0.0.1")) warn.Add("The database host is localhost. In the Docker setup it should be the postgres container name.");
            if (isProd && string.IsNullOrWhiteSpace(redis)) warn.Add("REDIS_CONNECTION_STRING is not set: the cache falls back to memory (fine for one server, but sessions are cached per process).");
            if (isProd && (connectionString ?? "").Contains("Password=postgres", StringComparison.OrdinalIgnoreCase)) fatal.Add("The database password is the default 'postgres'.");
            return (fatal, warn);
        }
    }
}
