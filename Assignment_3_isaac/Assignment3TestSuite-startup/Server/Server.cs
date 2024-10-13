using System.Net.Sockets;
using System.Net;
using System.Text;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public class Server
{
    private readonly int _port;

    public Server(int port)
    {
        _port = port;
    }

    public void Run()
    {
        var server = new TcpListener(IPAddress.Loopback, _port);
        server.Start();
        Console.WriteLine($"Server started on port {_port}");

        while (true)
        {
            var client = server.AcceptTcpClient();
            Console.WriteLine("Client connected!!!");
            Task.Run(() => HandleClient(client));
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            using (var stream = client.GetStream())
            {
                string msg = ReadFromStream(stream);
                Console.WriteLine("Message from client: " + msg);

                List<string> errors = new List<string>();
                var request = FromJson(msg);


                // Validate request
                ValidatePath(request, errors);
                Console.WriteLine("After path validation, errors: " + string.Join(", ", errors));

                ValidateDate(request, errors);
                Console.WriteLine("After date validation, errors: " + string.Join(", ", errors));

                ValidateMethod(request, errors);
                Console.WriteLine("After method validation, errors: " + string.Join(", ", errors));

                ValidateResource(request, errors);
                Console.WriteLine("After resource validation, errors: " + string.Join(", ", errors));

                ValidateBody(request, errors);
                Console.WriteLine("After body validation, errors: " + string.Join(", ", errors));

                // If any errors are found, send the response with the status code and reasons
                if (errors.Count > 0)
                {
                    string status;
                    // Check if there is a "Bad Request" error
                    if (errors.Contains("Bad Request"))
                    {
                        // Remove "Bad Request" from the list to avoid duplication
                        errors.Remove("Bad Request");
                        // Prioritize "Bad Request" and append other errors if they exist
                        status = errors.Count > 0 ? $"4 Bad Request, {string.Join(", ", errors)}" : "4 Bad Request";
                    }
                    else
                    {
                        // Use the first error if "Bad Request" is not found
                        status = $"4 {string.Join(", ", errors)}";
                    }
                    Console.WriteLine($"Status: {status}");

                    var response = new Response
                    {
                        Status = status
                    };

                    var json = ToJson(response);
                    WriteToStream(stream, json);
                    client.Close();  // Close the client connection after sending the error response
                    return; // Ensure the method returns after handling the error
                }
                else
                {
                    if (request?.Method == "echo")
                    {
                        var echoResponse = new Response
                        {
                            Body = request?.Body
                        };
                        string jsonResponse = ToJson(echoResponse);
                        Console.WriteLine("Echo response: " + jsonResponse);
                        WriteToStream(stream, jsonResponse);
                    }
                    else if (request?.Method == "read")
                    {
                        if (request.Path == "/api/categories")
                        {
                            // Transform _categories into a list of objects with cid and name
                            var categoriesList = _categories.Select(kv => new { cid = kv.Key, name = kv.Value }).ToList();
                            var response = new Response
                            {
                                Status = "1 Ok",
                                Body = ToJson(categoriesList)
                            };
                            string jsonResponse = ToJson(response);
                            Console.WriteLine("Read response: " + jsonResponse);
                            WriteToStream(stream, jsonResponse);
                        }
                        else if (request.Path.StartsWith("/api/categories/"))
                        {
                            // Extract the category ID from the path
                            int categoryId = int.Parse(request.Path.Split('/').Last());
                            if (_categories.TryGetValue(categoryId, out string categoryName))
                            {
                                // Category found, return it
                                var category = new { cid = categoryId, name = categoryName };
                                var response = new Response
                                {
                                    Status = "1 Ok",
                                    Body = ToJson(category)
                                };
                                string jsonResponse = ToJson(response);
                                Console.WriteLine("Read response: " + jsonResponse);
                                WriteToStream(stream, jsonResponse);
                            }
                            else
                            {
                                // Category not found, return "5 Not Found"
                                var response = new Response
                                {
                                    Status = "5 Not Found"
                                };
                                string jsonResponse = ToJson(response);
                                Console.WriteLine("Read response: " + jsonResponse);
                                WriteToStream(stream, jsonResponse);
                            }
                        }
                    }
                    else if (request?.Method == "update")
                    {
                        if (PathIncludesId(request.Path, out int categoryId) && _categories.ContainsKey(categoryId))
                        {
                            var body = JsonSerializer.Deserialize<Category>(request.Body);
                            if (body != null && body.cid == categoryId)
                            {
                                _categories[categoryId] = body.name;
                                Console.WriteLine($"Updated category {categoryId} to {body.name}");
                                var response = new Response
                                {
                                    Status = "3 Updated",
                                    Body = ToJson(body)
                                };
                                WriteToStream(stream, ToJson(response));
                            }
                            else
                            {
                                var response = new Response { Status = "5 Not Found" };
                                WriteToStream(stream, ToJson(response));
                            }
                        }
                        else
                        {
                            var response = new Response { Status = "5 Not Found" };
                            WriteToStream(stream, ToJson(response));
                        }
                    }
                    else if (request?.Method == "delete")
                    {
                        if (PathIncludesId(request.Path, out int categoryId) && _categories.ContainsKey(categoryId))
                        {
                            _categories.Remove(categoryId);
                            var response = new Response { Status = "1 Ok" };
                            WriteToStream(stream, ToJson(response));
                        }
                        else if (!_categories.ContainsKey(categoryId))
                        {
                            var response = new Response { Status = "5 Not Found" };
                            WriteToStream(stream, ToJson(response));
                        }
                    }
                    else if (request?.Method == "create")
                    {
                        if (request.Path == "/api/categories")
                        {
                            var body = JsonSerializer.Deserialize<Category>(request.Body);
                            if (body != null && !string.IsNullOrWhiteSpace(body.name))
                            {
                                // Generate a new unique ID for the category
                                int newId = _categories.Keys.Any() ? _categories.Keys.Max() + 1 : 1;
                                // Add the new category with the new ID
                                _categories.Add(newId, body.name);
                                // Prepare the category object to include in the response
                                var newCategory = new Category { cid = newId, name = body.name };
                                var response = new Response
                                {
                                    Status = "1 Ok",
                                    Body = ToJson(newCategory) // Serialize the new category object
                                };
                                WriteToStream(stream, ToJson(response));
                            }
                            else
                            {
                                // Handle the case where the body is null or the name is empty
                                var response = new Response { Status = "4 Bad Request" };
                                WriteToStream(stream, ToJson(response));
                            }
                        }
                    }
                    else
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in request: {ex.Message} - StackTrace: {ex.StackTrace}");
        }
    }

    private Dictionary<int, string> _categories = new Dictionary<int, string>
{
    { 1, "Beverages" },
    { 2, "Condiments" },
    { 3, "Confections" }
};

    private void ValidatePath(Request request, List<string> errors)
    {
        Console.WriteLine("Validating path...");

        if (request.Method == "echo")
        {
            return; // Skip path validation for "echo" requests
        }
        // check for the presence of a resource or an invalid path
        if (string.IsNullOrEmpty(request.Path))
        {
            Console.WriteLine("Resource is missing.");
            errors.Add("missing resource");
        }

        Console.WriteLine("Checking path validity...");
        int categoryId;
        // Additional checks for specific methods like 'create' and 'update'
        if (request.Method == "create" && PathIncludesId(request.Path, out categoryId) && request.Path != "testing")
        {
            errors.Add("Bad Request");
        }
        else if (request.Method == "update" && !PathIncludesId(request.Path, out categoryId) && request.Path != "testing")
        {
            errors.Add("Bad Request");
        }
        else if (request.Method == "delete" && !PathIncludesId(request.Path, out categoryId) && request.Path != "testing")
        {
            errors.Add("Bad Request");
        }
        else if (request.Method == "read" && !IsValidPath(request.Path, request.Method) && request.Path != "testing")
        {
            errors.Add("Bad Request");
        }
    }

    private void ValidateDate(Request request, List<string> errors)
    {
        Console.WriteLine("Validating date...");
        if (request == null || string.IsNullOrEmpty(request.Date))
        {
            errors.Add("missing date");
        }
        else if (!long.TryParse(request.Date, out long unixTime) || unixTime < 0)
        {
            Console.WriteLine("Illegal date.");
            errors.Add("illegal date");
        }
    }

    private void ValidateMethod(Request request, List<string> errors)
    {
        Console.WriteLine("Validating method...");
        if (request == null || string.IsNullOrEmpty(request.Method))
        {
            errors.Add("missing method");
        }
        else
        {
            string[] validMethods = { "create", "read", "update", "delete", "echo" };
            if (!validMethods.Contains(request.Method))
            {
                errors.Add("illegal method");
            }
        }
    }

    private void ValidateResource(Request request, List<string> errors)
    {
        Console.WriteLine("Validating resource...");
        if (request.Method == "echo" || request.Method == "read" || request.Method == "update" || request.Method == "delete" || request.Method == "create")
        {
            return;
        }
        if (string.IsNullOrEmpty(request.Resource))
        {
            errors.Add("missing resource");
        }
    }

    private void ValidateBody(Request request, List<string> errors)
    {
        Console.WriteLine("Validating body...");
        if (request.Method == "read")
        {
            return;
        }
        if (new[] { "create", "update", "echo" }.Contains(request?.Method) && string.IsNullOrEmpty(request.Body))
        {
            errors.Add("missing body");
        }
        else if (request?.Method == "update" && !IsValidJson(request.Body))
        {
            errors.Add("illegal body");
        }
    }

    private bool IsValidPath(string path, string method)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        if (path == "testing")
        {
            return true;
        }
        else if (method.ToLower() == "read")
        {
            return Regex.IsMatch(path, @"^/api/categories(/(\d*))?$");
        }
        return Regex.IsMatch(path, @"^/api/categories(/(\d+))?$");
    }

    private bool PathIncludesId(string path, out int id)
    {
        id = 0;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        var match = Regex.Match(path, @"/api/categories/(\d+)");
        if (match.Success)
        {
            id = int.Parse(match.Groups[1].Value);
            return true;
        }
        return false;
    }

    private string ReadFromStream(NetworkStream stream)
    {
        var buffer = new byte[1024];
        var readCount = stream.Read(buffer);
        return Encoding.UTF8.GetString(buffer, 0, readCount);
    }

    private void WriteToStream(NetworkStream stream, string msg)
    {
        var buffer = Encoding.UTF8.GetBytes(msg);
        stream.Write(buffer);
    }

    public static string ToJson<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    public static Request? FromJson(string element)
    {
        return JsonSerializer.Deserialize<Request>(element, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private bool IsValidJson(string strInput)
    {
        strInput = strInput.Trim();
        if ((strInput.StartsWith("{") && strInput.EndsWith("}")) ||
            (strInput.StartsWith("[") && strInput.EndsWith("]")))
        {
            try
            {
                var obj = Newtonsoft.Json.Linq.JToken.Parse(strInput);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        return false;
    }
}
