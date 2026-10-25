using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Atap.Cli;

[assembly: SupportedOSPlatform("windows")]

return await CliApp.RunAsync(args, Console.Out, Console.Error, input: Console.In);

namespace Atap.Cli
{
    /// <summary>Exit codes, the same for every command.</summary>
    internal static class ExitCodes
    {
        public const int Ok = 0;
        public const int Usage = 1;

        /// <summary>Atap is not running, or its local API is off.</summary>
        public const int NotRunning = 2;

        /// <summary>Atap answered with an error code.</summary>
        public const int Rejected = 3;
        public const int Timeout = 4;
    }

    /// <summary>
    /// Parses the command line and runs one command: ping, push, end or alert. It builds each request itself and
    /// leaves checking the values to Atap, which answers with a short error code when one is wrong.
    /// </summary>
    internal static class CliApp
    {
        public const int MaxJsonBytes = 8 * 1024;

        public const string Usage = """
            Usage: atap <command> [options]

            Commands:
              push --id <id> --title <text> [--subtitle <text>] [--progress <0-100>]
                   [--state running|done|failed] [--icon <name>] [--accent <name>] [--ttl <10m>]
                           Show or update progress on the notch. Use the same id to update it.
              push --json -    Read one push as JSON from standard input.
              end --id <id>    Remove a push before its time is up.
              alert --title <text> [--subtitle <text>] [--icon <name>]
                           Show an alert on the notch.
              ping             Check that Atap is running with its local API on.
              --version        Show the version of this tool.

            Icons: info, check, warning, error, download, upload, sync, backup, server, build, timer, bell, terminal.
            Accents: teal, amber, coral, blue, violet. Time to live: 10s to 24h, 15m when left out.
            Pushes show only while the Push widget is on in Atap.

            Exit codes: 0 ok, 1 usage, 2 Atap not running or API off, 3 rejected, 4 timeout.
            """;

        private static readonly string[] PushOptions = ["id", "title", "subtitle", "progress", "state", "icon", "accent", "ttl"];
        private static readonly string[] EndOptions = ["id"];
        private static readonly string[] AlertOptions = ["title", "subtitle", "icon"];

        public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IPushClient? client = null, TextReader? input = null)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                (args.Length == 0 ? error : output).WriteLine(Usage);
                return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Ok;
            }

            client ??= new PushClient();
            switch (args[0])
            {
                case "--version" when args.Length == 1:
                    output.WriteLine($"atap {Version}");
                    return ExitCodes.Ok;

                case "ping" when args.Length == 1:
                    return Report(await client.PingAsync(), output, error);

                case "push" when args is [_, "--json", "-"]:
                    return await SendJsonAsync(input ?? TextReader.Null, client, output, error);

                case "push":
                    return await SendAsync("push", args, PushOptions, ["id", "title"], client, "Pushed.", output, error);

                case "end":
                    return await SendAsync("end", args, EndOptions, ["id"], client, "Ended.", output, error);

                case "alert":
                    return await SendAsync("alert", args, AlertOptions, ["title"], client, "Shown.", output, error);

                default:
                    return UsageError($"Unknown command or extra arguments: {string.Join(' ', args)}", error);
            }
        }

        /// <summary>The informational version without build metadata.</summary>
        public static string Version
        {
            get
            {
                var informational = typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                var metadata = informational.IndexOf('+', StringComparison.Ordinal);
                return metadata >= 0 ? informational[..metadata] : informational;
            }
        }

        /// <summary>
        /// Builds a request from <c>--name value</c> pairs. Progress is sent as a number, everything else as text,
        /// in the field names Atap reads. Null with a message when the options are wrong.
        /// </summary>
        public static byte[]? BuildRequest(string op, IReadOnlyList<string> options, string[] allowed, string[] required, out string? problem)
        {
            problem = null;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < options.Count; i += 2)
            {
                var name = options[i];
                if (!name.StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(name[2..]))
                {
                    problem = $"Unknown option for {op}: {name}";
                    return null;
                }
                if (i + 1 >= options.Count)
                {
                    problem = $"The option {name} needs a value.";
                    return null;
                }
                values[name[2..]] = options[i + 1];
            }
            foreach (var name in required)
            {
                if (!values.ContainsKey(name))
                {
                    problem = $"{op} needs --{name}.";
                    return null;
                }
            }

            double? progress = null;
            if (values.TryGetValue("progress", out var progressText))
            {
                if (!double.TryParse(progressText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    problem = "--progress must be a number from 0 to 100.";
                    return null;
                }
                progress = number;
            }

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("op", op);
                foreach (var name in allowed)
                {
                    if (name == "progress" && progress is { } value)
                        writer.WriteNumber("progress", value);
                    else if (name != "progress" && values.TryGetValue(name, out var text))
                        writer.WriteString(name, text);
                }
                writer.WriteEndObject();
            }
            return buffer.WrittenSpan.ToArray();
        }

        /// <summary>
        /// One push read from standard input. It must be a JSON object. A missing "op" is taken as "push", so the
        /// object can be the push alone. Null with a message otherwise.
        /// </summary>
        public static byte[]? BuildJsonRequest(string text, out string? problem)
        {
            problem = null;
            if (Encoding.UTF8.GetByteCount(text) > MaxJsonBytes)
            {
                problem = "The JSON is over 8192 bytes.";
                return null;
            }
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    problem = "The JSON must be one object, such as {\"id\":\"build\",\"title\":\"Building\"}.";
                    return null;
                }
                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    writer.WriteStartObject();
                    if (!document.RootElement.TryGetProperty("op", out _))
                        writer.WriteString("op", "push");
                    foreach (var property in document.RootElement.EnumerateObject())
                        property.WriteTo(writer);
                    writer.WriteEndObject();
                }
                return buffer.WrittenSpan.ToArray();
            }
            catch (JsonException)
            {
                problem = "The input is not valid JSON.";
                return null;
            }
        }

        private static async Task<int> SendAsync(
            string op, string[] args, string[] allowed, string[] required, IPushClient client, string done, TextWriter output, TextWriter error)
        {
            var json = BuildRequest(op, args[1..], allowed, required, out var problem);
            if (json is null)
                return UsageError(problem!, error);
            return Report(await client.SendAsync(json, done), output, error);
        }

        private static async Task<int> SendJsonAsync(TextReader input, IPushClient client, TextWriter output, TextWriter error)
        {
            var json = BuildJsonRequest(await input.ReadToEndAsync(), out var problem);
            if (json is null)
                return UsageError(problem!, error);
            return Report(await client.SendAsync(json, "Pushed."), output, error);
        }

        private static int Report(CliResult result, TextWriter output, TextWriter error)
        {
            (result.ExitCode == ExitCodes.Ok ? output : error).WriteLine(result.Message);
            return result.ExitCode;
        }

        private static int UsageError(string message, TextWriter error)
        {
            error.WriteLine(message);
            error.WriteLine("Run atap --help to see every command.");
            return ExitCodes.Usage;
        }
    }
}
