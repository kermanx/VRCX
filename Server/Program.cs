using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRCX;


namespace Server
{
    internal class Program
    {
        private static readonly Dictionary<string, object> CreatedObjects = new();
        private static string? _rootDir;

        static async Task Main(string[] args)
        {

            await CallDotNetMethodAsync("ProgramElectron", "PreInit", new List<object> { "version", new List<string> { "--config=/VRCX/" }.ToArray() });
            await CallDotNetMethodAsync("VRCXStorage", "Load", null);
            await CallDotNetMethodAsync("ProgramElectron", "Init", null);
            await CallDotNetMethodAsync("SQLiteLegacy", "Init", null);
            await CallDotNetMethodAsync("AppApiElectron", "Init", null);
            await CallDotNetMethodAsync("Discord", "Init", null);
            await CallDotNetMethodAsync("WebApi", "Init", null);
            await CallDotNetMethodAsync("LogWatcher", "Init", null);

            HttpListener listener = new();
            string prefix = $"http://+:{Environment.GetEnvironmentVariable("VRCX_PORT") ?? "3333"}/";
            listener.Prefixes.Add(prefix);
            listener.Start();
            Console.WriteLine($"Server is running on {prefix}");

            _rootDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..");
            // Normalize the path to remove redundant segments
            _rootDir = Path.GetFullPath(_rootDir);
            Console.WriteLine($"rootDir {_rootDir}");

            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
        }

        private static async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                if (request.HttpMethod == "GET")
                {
                    await HandleGetRequestAsync(request, response);
                }
                else if (request.HttpMethod == "POST")
                {
                    await HandlePostRequestAsync(request, response);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await WriteResponseAsync(response, "Method not allowed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteResponseAsync(response, "Internal server error");
            }
        }

        private static async Task HandleGetRequestAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            string rootDir = _rootDir;
            string url = request.Url.AbsolutePath == "/" ? "/index.html" : request.Url.AbsolutePath;

            if (url == "/favicon.ico")
            {
                string iconPath = Path.Combine(rootDir, "VRCX.ico");
                if (File.Exists(iconPath))
                {
                    response.ContentType = "image/x-icon";
                    await WriteResponseAsync(response, await File.ReadAllBytesAsync(iconPath));
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    await WriteResponseAsync(response, "File not found");
                }
                return;
            }

            string filePath = Path.Combine(rootDir, "build", "html", url.TrimStart('/'));
            if (!filePath.StartsWith(rootDir) || !File.Exists(filePath))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteResponseAsync(response, "File not found");
                return;
            }

            string contentType = Path.GetExtension(filePath) switch
            {
                ".js" => "application/javascript",
                ".css" => "text/css",
                ".html" => "text/html",
                ".json" => "application/json",
                _ => "application/octet-stream"
            };
            response.ContentType = contentType;
            await WriteResponseAsync(response, await File.ReadAllBytesAsync(filePath));
        }

        private static async Task HandlePostRequestAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VRCX_PASSWORD")))
            {
                string authHeader = request.Headers["Authorization"];
                if (authHeader != $"Bearer {Environment.GetEnvironmentVariable("VRCX_PASSWORD")}")
                {
                    Console.WriteLine("Unauthorized access attempt");
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    await WriteResponseAsync(response, "Unauthorized");
                    return;
                }
            }

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            try
            {
                var options = new JsonSerializerOptions
                {
                    TypeInfoResolver = JsonContext.Default
                };

                var requestData = JsonSerializer.Deserialize<Dictionary<string, object>>(body, JsonContext.Default.DictionaryStringObject);
                string className = requestData["className"].ToString();
                string methodName = requestData["methodName"].ToString();
                var args = JsonSerializer.Deserialize<List<object>>(requestData["args"].ToString(), JsonContext.Default.ListObject);

                var result = await CallDotNetMethodAsync(className, methodName, args);
                response.ContentType = "application/json";
                var apiResponse = new ApiResponse { status = "success", result = result };
                await WriteResponseAsync(response, JsonSerializer.Serialize(apiResponse, options));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing request: {ex}");
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteResponseAsync(response, $"Error processing request: {ex.Message}");
            }
        }

        private static object GetDotNetObject(string className)
        {
            if (!CreatedObjects.ContainsKey(className))
            {
                Console.WriteLine($"Creating new instance of {className}");
                var type = className switch
                {
                    "ProgramElectron" => typeof(ProgramElectron),
                    "VRCXStorage" => typeof(VRCXStorage),
                    "SQLiteLegacy" => typeof(SQLiteLegacy),
                    "AppApiElectron" => typeof(AppApiElectron),
                    "Discord" => typeof(Discord),
                    "WebApi" => typeof(WebApi),
                    "LogWatcher" => typeof(LogWatcher),
                    "SharedVariable" => typeof(SharedVariable),
                    _ => null
                };
                if (type == null)
                    throw new Exception($"Class {className} not found");
                CreatedObjects[className] = Activator.CreateInstance(type);
            }
            return CreatedObjects[className];
        }

        private static async Task<object> CallDotNetMethodAsync(string className, string methodName, List<object> args)
        {
            var obj = GetDotNetObject(className);
            var method = obj.GetType().GetMethod(methodName);
            if (method == null)
                throw new Exception($"Method {methodName} does not exist on class {className}");

            var processedArgs = args?.ConvertAll(arg =>
            {
                if (arg is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("__is_map__", out _))
                    {
                        var dictionary = new Dictionary<string, object>();
                        foreach (var property in jsonElement.EnumerateObject())
                        {
                            if (property.Name != "__is_map__")
                            {
                                dictionary[property.Name] = property.Value.Deserialize<object>();
                            }
                        }
                        return dictionary;
                    }
                    else if (jsonElement.ValueKind == JsonValueKind.String)
                    {
                        return jsonElement.GetString();
                    }
                    else if (jsonElement.ValueKind == JsonValueKind.Number)
                    {
                        // Adjust numeric type conversion
                        if (jsonElement.TryGetInt32(out var intValue))
                            return intValue;
                        if (jsonElement.TryGetDouble(out var doubleValue))
                            return doubleValue;
                    }
                    else if (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False)
                    {
                        return jsonElement.GetBoolean();
                    }
                }
                return arg;
            });

            var result = method.Invoke(obj, processedArgs?.ToArray());
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                var taskResultProperty = task.GetType().GetProperty("Result");
                return taskResultProperty?.GetValue(task);
            }
            return result;
        }

        private static async Task WriteResponseAsync(HttpListenerResponse response, string content)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            await using var output = response.OutputStream;
            await output.WriteAsync(buffer, 0, buffer.Length);
        }

        private static async Task WriteResponseAsync(HttpListenerResponse response, byte[] content)
        {
            response.ContentLength64 = content.Length;
            await using var output = response.OutputStream;
            await output.WriteAsync(content, 0, content.Length);
        }
    }

    internal class ApiResponse
    {
        public string status { get; set; }
        public object result { get; set; }
    }

    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(List<object>))]
    [JsonSerializable(typeof(ApiResponse))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(object))] // For polymorphic serialization
    [JsonSerializable(typeof(Task<double>))]
    [JsonSerializable(typeof(Task<string>))]
    [JsonSerializable(typeof(Task<bool>))]
    [JsonSerializable(typeof(Task<object>))]
    [JsonSerializable(typeof(Task))]
    [JsonSerializable(typeof(string[][]))] // Add missing type
    [JsonSerializable(typeof(Dictionary<string, string>))] // Add common type
    internal partial class JsonContext : JsonSerializerContext
    {
    }
}
